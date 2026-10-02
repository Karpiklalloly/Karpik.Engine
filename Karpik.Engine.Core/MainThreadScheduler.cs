using System.Collections.Concurrent;
using Karpik.Jobs;

using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core;

public class MainThreadScheduler : IDisposable
{
    private readonly ConcurrentQueue<Action> _actions = new();
    private readonly int _mainThreadId;
    private readonly ILogger<MainThreadScheduler> _logger;
    private readonly ILoggerFactory? _ownedLoggerFactory;

    public MainThreadScheduler(int mainThreadId)
        : this(mainThreadId, HostLogging.CreateDefaultFactory(), ownsFactory: true)
    {
    }

    public MainThreadScheduler(int mainThreadId, ILoggerFactory loggerFactory)
        : this(mainThreadId, loggerFactory, ownsFactory: false)
    {
    }

    private MainThreadScheduler(int mainThreadId, ILoggerFactory loggerFactory, bool ownsFactory)
    {
        _mainThreadId = mainThreadId;
        _logger = loggerFactory.CreateLogger<MainThreadScheduler>();
        _ownedLoggerFactory = ownsFactory ? loggerFactory : null;
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

    public Task ScheduleAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _actions.Enqueue(() =>
        {
            try
            {
                _ = CompleteAsync(action(), completion);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
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
                _logger.LogError(e, "Main-thread action failed");
                throw;
            }
        }
    }

    public void Dispose() => _ownedLoggerFactory?.Dispose();

    private static async Task CompleteAsync(Task task, TaskCompletionSource completion)
    {
        try
        {
            await task.ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }
}
