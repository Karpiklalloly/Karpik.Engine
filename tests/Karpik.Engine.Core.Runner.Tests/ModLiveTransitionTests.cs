using Karpik.Engine.Core;
using Xunit;
using static Karpik.Engine.Core.ModLiveTransition;

public sealed class ModLiveTransitionTests
{
    private static readonly Operation Attempt = new(1, 7, 8);
    private static readonly Participant Server = new(11, NeedsBaseline: false);
    private static readonly Participant Client = new(22, NeedsBaseline: true);
    private static readonly Participant SecondClient = new(33, NeedsBaseline: true);

    [Fact]
    public void StepsRequireIssuedFactsFromTheFixedCohortBeforeCommitAndResume()
    {
        var transition = new ModLiveTransition(Attempt.Basis);
        Participant[] participants = [Server, Client, SecondClient];
        Assert.Equal(Admission.Started, transition.TryBegin(Attempt, participants));
        participants[2] = new(44, NeedsBaseline: true);
        Assert.Equal(Admission.Busy, transition.TryBegin(new(2, 7, 9), [Server, Client]));
        Assert.False(transition.TryCaptureAcceptedRevision(out _));

        Assert.False(transition.Receive(Attempt, Server.Id, Fact.Drained));
        Issue(transition, Command.Drain);
        Assert.False(transition.Receive(Attempt with { Id = 2 }, Server.Id, Fact.Drained));
        Assert.False(transition.Receive(Attempt with { Basis = 6 }, Server.Id, Fact.Drained));
        Assert.False(transition.Receive(Attempt with { Target = 9 }, Server.Id, Fact.Drained));
        Assert.False(transition.Receive(Attempt, 44, Fact.Drained));
        Assert.True(transition.Receive(Attempt, Server.Id, Fact.Drained));
        Assert.False(transition.Receive(Attempt, Server.Id, Fact.Drained));
        Assert.True(transition.Receive(Attempt, Client.Id, Fact.Drained));
        Assert.Equal(default, transition.Step());
        Assert.True(transition.Receive(Attempt, SecondClient.Id, Fact.Drained));

        Assert.False(transition.Receive(Attempt, Server.Id, Fact.Ready));
        Issue(transition, Command.Prepare);
        Assert.False(transition.Receive(Attempt, Server.Id, Fact.Drained));
        Assert.False(transition.Receive(Attempt, Server.Id, Fact.Committed));
        Assert.True(transition.Receive(Attempt, SecondClient.Id, Fact.Ready));
        Assert.True(transition.Receive(Attempt, Server.Id, Fact.Ready));
        Assert.False(transition.Receive(Attempt, Server.Id, Fact.Rejected));
        Assert.False(transition.Receive(Attempt, Server.Id, Fact.Ready));
        Assert.Equal(default, transition.Step());
        Assert.True(transition.Receive(Attempt, Client.Id, Fact.Ready));

        Assert.False(transition.Receive(Attempt, Server.Id, Fact.Committed));
        Issue(transition, Command.Commit);
        Assert.True(transition.Receive(Attempt, Server.Id, Fact.Committed));
        Assert.True(transition.Receive(Attempt, Client.Id, Fact.Committed));
        Assert.False(transition.Receive(Attempt, Client.Id, Fact.Committed));
        Assert.False(transition.Receive(Attempt, Client.Id, Fact.BaselineApplied));
        Assert.Equal(default, transition.Step());
        Assert.True(transition.Receive(Attempt, SecondClient.Id, Fact.Committed));
        Assert.False(transition.TryCaptureAcceptedRevision(out _));

        Assert.False(transition.Receive(Attempt, Client.Id, Fact.BaselineApplied));
        Issue(transition, Command.Baseline);
        Assert.False(transition.Receive(Attempt, Server.Id, Fact.BaselineApplied));
        Assert.True(transition.Receive(Attempt, Client.Id, Fact.BaselineApplied));
        Assert.False(transition.Receive(Attempt, Client.Id, Fact.BaselineApplied));
        Assert.Equal(default, transition.Step());
        Assert.True(transition.Receive(Attempt, SecondClient.Id, Fact.BaselineApplied));

        Assert.False(transition.Complete(Attempt, Command.Resume));
        Issue(transition, Command.Resume);
        Assert.False(transition.TryCaptureAcceptedRevision(out _));
        Assert.Equal(Admission.Busy, transition.TryBegin(new(2, 8, 9), [Server, Client]));
        Assert.False(transition.Complete(Attempt with { Target = 9 }, Command.Resume));
        Assert.False(transition.Complete(Attempt, Command.Abort));
        Assert.True(transition.Complete(Attempt, Command.Resume));
        Assert.False(transition.Complete(Attempt, Command.Resume));
        Assert.True(transition.TryCaptureAcceptedRevision(out ulong revision));
        Assert.Equal(Attempt.Target, revision);
        Assert.Equal(Admission.Invalid, transition.TryBegin(new(2, 7, 9), [Server, Client]));
        Assert.Equal(Admission.Started, transition.TryBegin(new(2, 8, 9), [Server, Client]));
    }

    [Theory]
    [InlineData(0)] // before drain issuance
    [InlineData(1)] // draining
    [InlineData(2)] // preparing
    [InlineData(3)] // all Ready, decision not yet issued
    [InlineData(4)] // Commit issued, no acknowledgments
    [InlineData(5)] // waiting for baseline
    [InlineData(6)] // Resume pending
    [InlineData(7)] // Resume issued, handoff not complete
    public void StopCancelAndTimeoutSplitAtTheSingleCommitDecision(int checkpoint)
    {
        foreach (var interruption in new[] { Interruption.Stop, Interruption.Cancel, Interruption.Timeout })
        {
            var transition = AtCheckpoint(checkpoint);
            bool beforeCommit = checkpoint < 4;
            var expected = beforeCommit ? Command.Abort : Command.FailStop;
            Assert.False(transition.Interrupt(Attempt with { Id = 2 }, interruption));
            Assert.True(transition.Interrupt(Attempt, interruption));
            Assert.False(transition.Interrupt(Attempt, interruption));
            Assert.False(transition.Receive(Attempt, Client.Id, Fact.Committed));
            Assert.False(transition.Receive(Attempt, Client.Id, Fact.BaselineApplied));
            Assert.False(transition.Complete(Attempt, expected));
            Issue(transition, expected);
            Assert.False(transition.TryCaptureAcceptedRevision(out _));

            Operation retry = Attempt with { Id = 2 };
            if (beforeCommit)
            {
                Assert.Equal(Admission.Busy, transition.TryBegin(retry, [Server, Client]));
                Assert.True(transition.Complete(Attempt, Command.Abort));
                Assert.True(transition.TryCaptureAcceptedRevision(out ulong revision));
                Assert.Equal(Attempt.Basis, revision);
                Assert.Equal(Admission.Invalid, transition.TryBegin(Attempt, [Server, Client]));
                Assert.Equal(Admission.Started, transition.TryBegin(retry, [Server, Client]));
                Assert.Equal(new Instruction(retry, Command.Drain), transition.Step());
                Assert.False(transition.Receive(Attempt, Server.Id, Fact.Drained));
                Assert.False(transition.Interrupt(Attempt, interruption));
            }
            else
            {
                Assert.False(transition.Complete(Attempt, Command.Abort));
                Assert.False(transition.Complete(Attempt, Command.Resume));
                Assert.Equal(Admission.FailStopped, transition.TryBegin(retry, [Server, Client]));
            }
        }
    }

    [Fact]
    public void ARejectedPrepareOrLostParticipantNeverShrinksTheCohort()
    {
        var preparing = AtCheckpoint(2);
        Assert.False(preparing.Receive(Attempt, 44, Fact.Rejected));
        Assert.True(preparing.Receive(Attempt, Client.Id, Fact.Rejected));
        Issue(preparing, Command.Abort);

        var ready = AtCheckpoint(3);
        Assert.False(ready.Receive(Attempt, 44, Fact.Lost));
        Assert.True(ready.Receive(Attempt, Client.Id, Fact.Lost));
        Issue(ready, Command.Abort);

        var committed = AtCheckpoint(4);
        Assert.True(committed.Receive(Attempt, Client.Id, Fact.Lost));
        Issue(committed, Command.FailStop);
    }

    [Fact]
    public void InvalidAdmissionDoesNotConsumeTheOperationOrBlockCapture()
    {
        var transition = new ModLiveTransition(0);
        Operation first = new(1, 0, 1);
        Assert.Equal(Admission.Invalid, transition.TryBegin(first with { Id = 0 }, [Client]));
        Assert.Equal(Admission.Invalid, transition.TryBegin(first with { Target = 0 }, [Client]));
        Assert.Equal(Admission.Invalid, transition.TryBegin(first, []));
        Assert.Equal(Admission.Invalid, transition.TryBegin(first, [Server]));
        Assert.Equal(Admission.Invalid, transition.TryBegin(first, [Client, Client]));
        Assert.Equal(Admission.Invalid, transition.TryBegin(first, [Client with { Id = 0 }]));
        Assert.Equal(default, transition.Step());
        Assert.True(transition.TryCaptureAcceptedRevision(out ulong revision));
        Assert.Equal(0UL, revision);
        Assert.Equal(Admission.Started, transition.TryBegin(first, [Client]));
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void AdmissionBoundsTheCohort(int count, bool admissible)
    {
        var transition = new ModLiveTransition(Attempt.Basis);
        var cohort = new Participant[count];
        for (int i = 0; i < count; i++) cohort[i] = new((ulong)(i + 1), NeedsBaseline: i > 0);

        Assert.Equal(admissible ? Admission.Started : Admission.Invalid, transition.TryBegin(Attempt, cohort));
    }

    private static ModLiveTransition AtCheckpoint(int checkpoint)
    {
        var transition = new ModLiveTransition(Attempt.Basis);
        Assert.Equal(Admission.Started, transition.TryBegin(Attempt, [Server, Client]));
        if (checkpoint < 1) return transition;
        Issue(transition, Command.Drain);
        if (checkpoint < 2) return transition;
        AllReply(transition, Fact.Drained);
        Issue(transition, Command.Prepare);
        if (checkpoint < 3) return transition;
        AllReply(transition, Fact.Ready);
        if (checkpoint < 4) return transition;
        Issue(transition, Command.Commit);
        if (checkpoint < 5) return transition;
        AllReply(transition, Fact.Committed);
        Issue(transition, Command.Baseline);
        if (checkpoint < 6) return transition;
        Assert.True(transition.Receive(Attempt, Client.Id, Fact.BaselineApplied));
        if (checkpoint < 7) return transition;
        Issue(transition, Command.Resume);
        return transition;
    }

    private static void AllReply(ModLiveTransition transition, Fact fact)
    {
        Assert.True(transition.Receive(Attempt, Server.Id, fact));
        Assert.True(transition.Receive(Attempt, Client.Id, fact));
    }

    private static void Issue(ModLiveTransition transition, Command command)
    {
        Assert.Equal(new Instruction(Attempt, command), transition.Step());
        Assert.Equal(default, transition.Step());
    }
}
