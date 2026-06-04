namespace Karpik.Jobs;

internal class JobCompletion
{
    private int _remaining;
    private readonly ManualResetEventSlim _event;
    private Action? _continuation;
    private Exception? _exception;
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
    
    internal void SetException(Exception ex)
    {
        lock (_lock)
        {
            _exception = ex;
        }
    }
    
    public void ThrowIfFailed()
    {
        if (_exception is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_exception).Throw();
        }
    }
}

internal sealed class JobCompletion<T> : JobCompletion
{
    internal T Result { get; private set; } = default!;

    public JobCompletion(int initialCount) : base(initialCount) { }

    public void SetResult(T result)
    {
        Result = result;
    }
}
