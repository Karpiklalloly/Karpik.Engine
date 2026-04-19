using Karpik.Jobs;

namespace Karpik.Engine.Core;

public static class Job
{
    internal static JobSystem JobSystem => _jobSystem;
    private static JobSystem _jobSystem = null!;
    
    public static void Initialize(JobSystem jobSystem)
    {
        _jobSystem?.WaitForCompletion();
        _jobSystem?.Shutdown();
        _jobSystem = jobSystem;
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