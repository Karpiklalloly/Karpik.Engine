namespace Karpik.Jobs;

internal static unsafe class JobExecutor<TJob>
    where TJob : unmanaged, IJob
{
    internal static void Execute(void* payload)
    {
        ((TJob*)payload)->Execute();
    }
}
