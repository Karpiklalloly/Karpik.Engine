namespace Karpik.Jobs;

internal static unsafe class JobForExecutor<TJob>
    where TJob : unmanaged, IJobFor
{
    internal static void Execute(void* payload, int index)
    {
        ((TJob*)payload)->Execute(index);
    }
}
