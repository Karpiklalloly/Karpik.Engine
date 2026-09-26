using Karpik.Jobs;

namespace Karpik.Engine.Core;

public static class Job
{
    internal static JobSystem JobSystem => _jobSystem;
    private static readonly SemaphoreSlim LifecycleGate = new(1, 1);
    private static JobSystem _jobSystem = null!;
    
    public static void Initialize(JobSystem jobSystem)
    {
        LifecycleGate.Wait();
        try
        {
            _jobSystem?.WaitForCompletion();
            _jobSystem?.Shutdown();
            _jobSystem = jobSystem;
        }
        finally
        {
            LifecycleGate.Release();
        }
    }

    internal static void ShutdownIfCurrent(JobSystem jobSystem)
    {
        LifecycleGate.Wait();
        try
        {
            if (!ReferenceEquals(_jobSystem, jobSystem)) return;
            jobSystem.WaitForCompletion();
            jobSystem.Shutdown();
            _jobSystem = null!;
        }
        finally
        {
            LifecycleGate.Release();
        }
    }
    
    public static JobHandle Run(Action action)
    {
        return _jobSystem.Enqueue(action);
    }
    
    public static JobHandle<T> Run<T>(Func<T> func)
    {
        return _jobSystem.Enqueue(func);
    }

    public static async JobHandle Run(Func<JobHandle> func)
    {
        var handle = await _jobSystem.Enqueue(func);
        await handle;
    }
    
    public static async JobHandle<T> Run<T>(Func<JobHandle<T>> func)
    {
        var result = await _jobSystem.Enqueue(func);
        return await result;
    }

    internal static void Wait()
    {
        _jobSystem.WaitForCompletion();
    }
}
