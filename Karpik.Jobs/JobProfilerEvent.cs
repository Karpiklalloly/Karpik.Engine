namespace Karpik.Jobs;

public readonly struct JobProfilerEvent
{
    internal JobProfilerEvent(
        ValueJobHandle handle,
        JobDescriptorKind kind,
        int dependencyCount,
        JobBatchInfo batchInfo,
        long timestamp)
    {
        Handle = handle;
        Kind = kind;
        DependencyCount = dependencyCount;
        BatchInfo = batchInfo;
        Timestamp = timestamp;
    }

    public ValueJobHandle Handle { get; }
    public JobDescriptorKind Kind { get; }
    public int DependencyCount { get; }
    public JobBatchInfo BatchInfo { get; }
    public long Timestamp { get; }
}
