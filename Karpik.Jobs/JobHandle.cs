namespace Karpik.Jobs;

public readonly struct JobHandle : IDisposable
{
    internal readonly JobCompletion Completion;
    private readonly CancellationTokenSource _cts;

    internal JobHandle(JobCompletion completion, CancellationTokenSource cts)
    {
        Completion = completion;
        _cts = cts;
    }

    public void Cancel() => _cts?.Cancel();
    public void Wait() => Completion?.Wait();
    public bool IsCompleted => Completion?.IsCompleted ?? true;
    public void Dispose() => _cts?.Dispose();
}