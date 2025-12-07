using System.Collections.Concurrent;

namespace Karpik.Engine.Shared;

public class MainThreadScheduler
{
    private readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();
    private readonly int _mainThreadId;

    public MainThreadScheduler(int mainThreadId)
    {
        _mainThreadId = mainThreadId;
    }
    
    public JobHandle<T> InvokeAsync<T>(Func<T> work)
    {
        if (Environment.CurrentManagedThreadId == _mainThreadId)
        {
            try 
            {
                return JobHandle<T>.FromResult(work());
            }
            catch (Exception ex)
            {
                return JobHandle<T>.FromException(ex);
            }
        }
        
        var t = new JobHandleCompletionSource<T>();

        _actions.Enqueue(() =>
        {
            try
            {
                var result = work();
                t.SetResult(result);
            }
            catch (Exception ex)
            {
                t.SetException(ex);
            }
        });

        return t.JobHandle;
    }
    
    public JobHandle InvokeAsync(Action work)
    {
        if (Environment.CurrentManagedThreadId == _mainThreadId)
        {
            try
            {
                work();
                return JobHandle.Completed;
            }
            catch (Exception ex)
            {
                return JobHandle.FromException(ex);
            }
        }
        
        var t = new JobHandleCompletionSource<bool>();

        _actions.Enqueue(() =>
        {
            try
            {
                work();
                t.SetResult(true);
            }
            catch (Exception ex)
            {
                t.SetException(ex);
            }
        });

        return (JobHandle)t.JobHandle;
    }

    public void Execute()
    {
        while (_actions.TryDequeue(out var action))
        {
            action();
        }
    }
}