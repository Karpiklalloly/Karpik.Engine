using System.Collections.Concurrent;
using Karpik.Jobs;

namespace Karpik.Engine.Core;

public class MainThreadScheduler
{
    private readonly ConcurrentQueue<Action> _actions = new();
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

    public void Schedule(Action action)
    {
        _actions.Enqueue(action);
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
            try
            {
                action();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}