using System.Runtime.CompilerServices;

namespace Karpik.Jobs;

[AsyncMethodBuilder(typeof(JobHandleMethodBuilder))]
public readonly struct JobHandle : IDisposable
{
    public static JobHandle Completed => new(new JobCompletion(0), null);
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
    public JobHandleAwaiter GetAwaiter() => new(this);
    
    public static JobHandle FromException(Exception exception)
    {
        var completion = new JobCompletion(0);
        completion.SetException(exception);
        return new JobHandle(completion, null);
    }
}

[AsyncMethodBuilder(typeof(JobHandleMethodBuilder<>))]
public readonly struct JobHandle<T> : IDisposable
{
    // Храним ссылку именно как Generic версию
    internal readonly JobCompletion<T> Completion;
    private readonly CancellationTokenSource _cts;

    internal JobHandle(JobCompletion<T> completion, CancellationTokenSource cts)
    {
        Completion = completion;
        _cts = cts;
    }

    public void Cancel() => _cts?.Cancel();
    public void Wait() => Completion?.Wait();
    public bool IsCompleted => Completion?.IsCompleted ?? true;
    public void Dispose() => _cts?.Dispose();

    public JobHandleAwaiter<T> GetAwaiter() => new JobHandleAwaiter<T>(this);
    public static JobHandle<T> FromResult(T result)
    {
        var completion = new JobCompletion<T>(0);
        completion.SetResult(result);
        return new JobHandle<T>(completion, null);
    }
    
    public static JobHandle<T> FromException(Exception exception)
    {
        var completion = new JobCompletion<T>(0);
        completion.SetException(exception);
        return new JobHandle<T>(completion, null);
    }
    
    public static explicit operator JobHandle(JobHandle<T> handle)
    {
        return new JobHandle(handle.Completion, handle._cts);
    }
}