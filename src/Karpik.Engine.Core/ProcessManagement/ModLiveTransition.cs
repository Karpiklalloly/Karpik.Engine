namespace Karpik.Engine.Core;

/// <summary>Cold control state for one session. Call only from its serialized host owner.</summary>
internal sealed class ModLiveTransition(ulong acceptedRevision)
{
    internal readonly record struct Operation(ulong Id, ulong Basis, ulong Target);
    // The owner supplies session-unique identities, including across participant replacements.
    internal readonly record struct Participant(ulong Id, bool NeedsBaseline);
    internal readonly record struct Instruction(Operation Operation, Command Command);

    internal enum Admission { Started, Busy, Invalid, FailStopped }
    internal enum Command { None, Drain, Prepare, Commit, Baseline, Resume, Abort, FailStop }
    internal enum Fact { None, Drained, Ready, Committed, BaselineApplied, Rejected, Lost }
    internal enum Interruption { Stop, Cancel, Timeout }

    private readonly record struct ParticipantState(bool NeedsBaseline, Fact Received);

    // Bound the cold snapshot (including the server); this is not a transport player limit.
    private const int MaxParticipants = 64;

    private ulong _acceptedRevision = acceptedRevision;
    private Operation _operation; // Retain the last ID after completion; never reuse it in this session.
    private Dictionary<ulong, ParticipantState>? _cohort;
    private int _baselineRecipients;
    private int _remaining;
    private Command _command;
    private bool _issued;
    private bool _commitDecided;

    internal Admission TryBegin(Operation operation, ReadOnlySpan<Participant> cohort)
    {
        if (_command == Command.FailStop) return Admission.FailStopped;
        if (_command != Command.None) return Admission.Busy;
        if (cohort.Length is 0 or > MaxParticipants ||
            operation.Id <= _operation.Id || operation.Basis != _acceptedRevision || operation.Target <= operation.Basis)
            return Admission.Invalid;

        var participants = new Dictionary<ulong, ParticipantState>(cohort.Length);
        int baselineRecipients = 0;
        foreach (var participant in cohort)
        {
            if (participant.Id == 0 || !participants.TryAdd(participant.Id, new(participant.NeedsBaseline, Fact.None)))
                return Admission.Invalid;
            if (participant.NeedsBaseline) baselineRecipients++;
        }
        if (baselineRecipients == 0) return Admission.Invalid;

        _operation = operation;
        _cohort = participants;
        _baselineRecipients = baselineRecipients;
        _commitDecided = false;
        SetCommand(Command.Drain);
        return Admission.Started;
    }

    // Returns each cohort-wide instruction once. Taking Commit is the decision, before dispatch.
    internal Instruction Step()
    {
        if (_command == Command.None || _issued) return default;
        _issued = true;
        if (_command == Command.Commit) _commitDecided = true;
        _remaining = _command == Command.Baseline ? _baselineRecipients : _cohort?.Count ?? 0;
        return new(_operation, _command);
    }

    internal bool Receive(Operation operation, ulong participant, Fact fact)
    {
        if (!IsActive(operation) || _cohort is not { } cohort || !cohort.TryGetValue(participant, out var state))
            return false;

        // Loss includes withdrawal of previously reported readiness and is unsolicited.
        if (fact == Fact.Lost) return InterruptActive();
        if (!_issued) return false;
        if (fact == Fact.Rejected && _command == Command.Prepare && state.Received != Fact.Ready)
            return InterruptActive();

        var (expected, next) = _command switch
        {
            Command.Drain => (Fact.Drained, Command.Prepare),
            Command.Prepare => (Fact.Ready, Command.Commit),
            Command.Commit => (Fact.Committed, Command.Baseline),
            Command.Baseline when state.NeedsBaseline => (Fact.BaselineApplied, Command.Resume),
            _ => default
        };
        if (expected == Fact.None || fact != expected || state.Received == fact) return false;

        cohort[participant] = state with { Received = fact };
        if (--_remaining == 0) SetCommand(next);
        return true;
    }

    internal bool Interrupt(Operation operation, Interruption reason) =>
        IsActive(operation) && reason is (Interruption.Stop or Interruption.Cancel or Interruption.Timeout) && InterruptActive();

    private bool InterruptActive()
    {
        SetCommand(_commitDecided ? Command.FailStop : Command.Abort);
        _cohort = null;
        return true;
    }

    // A host completion, not a worker fact: Abort cleanup or the Resume handoff has finished.
    // If pre-Commit Abort cleanup cannot restore safe operation, the owner must fail-stop, not complete it.
    internal bool Complete(Operation operation, Command completed)
    {
        if (!_issued || _command != completed || operation != _operation || completed is not (Command.Abort or Command.Resume))
            return false;

        if (completed == Command.Resume) _acceptedRevision = operation.Target;
        _cohort = null;
        SetCommand(Command.None);
        return true;
    }

    // Capture must run under the same owner serialization; this is not a reservation.
    internal bool TryCaptureAcceptedRevision(out ulong revision)
    {
        bool idle = _command == Command.None;
        revision = idle ? _acceptedRevision : default;
        return idle;
    }

    private bool IsActive(Operation operation) =>
        operation == _operation && _command is not (Command.None or Command.Abort or Command.FailStop);

    private void SetCommand(Command command)
    {
        _command = command;
        _issued = false;
    }
}
