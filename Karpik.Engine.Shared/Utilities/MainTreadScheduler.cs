using System.Collections.Concurrent;

namespace Karpik.Engine.Shared;

public class MainTreadScheduler
{
    private readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();
    private readonly int _mainThreadId;

    public MainTreadScheduler(int mainThreadId)
    {
        _mainThreadId = mainThreadId;
    }
    
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        if (Environment.CurrentManagedThreadId == _mainThreadId)
        {
            try 
            {
                return Task.FromResult(work());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }
        
        var tcs = new TaskCompletionSource<T>();

        _actions.Enqueue(() =>
        {
            try
            {
                var result = work();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                // Если упало - передаем ошибку вызывающему
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
    
    public Task InvokeAsync(Action work)
    {
        if (Environment.CurrentManagedThreadId == _mainThreadId)
        {
            try
            {
                work();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }
        
        var tcs = new TaskCompletionSource<bool>();

        _actions.Enqueue(() =>
        {
            try
            {
                work();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    public void Execute()
    {
        while (_actions.TryDequeue(out var action))
        {
            action();
        }
    }
}