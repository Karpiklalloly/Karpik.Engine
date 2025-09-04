namespace Karpik.Jobs;

internal sealed class JobCompletion
{
    private volatile int _remaining;
    private readonly ManualResetEventSlim _event;
    private Action? _continuation;
    private readonly Lock _lock = new();
    public bool IsCompleted => Volatile.Read(ref _remaining) == 0;

    public JobCompletion(int initialCount)
    {
        _remaining = initialCount;
        _event = new ManualResetEventSlim(initialCount == 0);
    }

    public void Signal()
    {
        if (Interlocked.Decrement(ref _remaining) > 0) return;
        lock (_lock)
        {
            if (!_event.IsSet)
            {
                _event.Set();
                _continuation?.Invoke();
            }
        }
    }

    public void Wait() => _event.Wait();

    public void AddContinuation(Action continuation)
    {
        lock (_lock)
        {
            if (IsCompleted)
            {
                continuation();
            }
            else
            {
                _continuation += continuation;
            }
        }
    }
}