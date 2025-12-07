namespace Karpik.Engine.Shared;

public static class Jobs
{
    private static JobSystem _jobSystem;

    public static void Initialize(JobSystem jobSystem)
    {
        _jobSystem = jobSystem;
    }
    
    public static JobHandle Run(Action action)
    {
        return _jobSystem.Enqueue(action);
    }
    
    public static JobHandle Run(Action action, Span<JobHandle> dependencies)
    {
        return _jobSystem.Enqueue(action, dependencies);
    }
    
    public static JobHandle<T> Run<T>(Func<T> func)
    {
        return _jobSystem.Enqueue(func);
    }
    
    public static JobHandle<T> Run<T>(Func<T> func, Span<JobHandle> dependencies)
    {
        return _jobSystem.Enqueue(func, dependencies);
    }
    
    public static void WaitForCompletion()
    {
        _jobSystem.WaitForCompletion();
    }
}