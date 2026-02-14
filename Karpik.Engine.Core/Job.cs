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
    
    public static JobHandle<T> Run<T>(Func<T> func)
    {
        return _jobSystem.Enqueue(func);
    }
    
    public static JobHandle Run(Action action)
    {
        return _jobSystem.Enqueue(action);
    }

    internal static void Wait()
    {
        _jobSystem.WaitForCompletion();
    }
}