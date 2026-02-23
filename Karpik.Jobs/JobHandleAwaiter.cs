using System.Runtime.CompilerServices;

namespace Karpik.Jobs;

public readonly struct JobHandleAwaiter : INotifyCompletion
{
    private readonly JobHandle _handle;

    public JobHandleAwaiter(JobHandle handle)
    {
        _handle = handle;
    }

    public bool IsCompleted => _handle.IsCompleted;

    public void GetResult()
    {
        _handle.Wait();
        _handle.Completion?.ThrowIfFailed(); 
    }

    public void OnCompleted(Action continuation)
    {
        if (_handle.Completion is not null)
        {
            _handle.Completion.AddContinuation(continuation);
        }
        else
        {
            continuation();
        }
    }
}

public readonly struct JobHandleAwaiter<T> : INotifyCompletion
{
    private readonly JobHandle<T> _handle;

    public JobHandleAwaiter(JobHandle<T> handle)
    {
        _handle = handle;
    }

    public bool IsCompleted => _handle.IsCompleted;

    public T GetResult()
    {
        _handle.Wait();
        _handle.Completion?.ThrowIfFailed();
            
        return _handle.Completion is not null ? _handle.Completion.Result : default;
    }

    public void OnCompleted(Action continuation)
    {
        if (_handle.Completion is not null)
            _handle.Completion.AddContinuation(continuation);
        else
            continuation();
    }
}