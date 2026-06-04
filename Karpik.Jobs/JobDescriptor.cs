namespace Karpik.Jobs;

internal struct JobDescriptor
{
    public JobDescriptorKind Kind;
    public int Generation;
    public int DependencyCount;
    public int RemainingDependencies;
    public int StartIndex;
    public int EndIndex;
    public int BatchIndex;
    public int BatchCount;
    public int PayloadByteOffset;
    public int PayloadByteLength;
    public int ExceptionIndex;
    public long ProfilerStartTimestamp;
    public long ProfilerEndTimestamp;

    internal void ResetForRent(JobDescriptorKind kind)
    {
        Kind = kind;
        DependencyCount = 0;
        RemainingDependencies = 0;
        StartIndex = 0;
        EndIndex = 0;
        BatchIndex = 0;
        BatchCount = 0;
        PayloadByteOffset = 0;
        PayloadByteLength = 0;
        ExceptionIndex = -1;
        ProfilerStartTimestamp = 0;
        ProfilerEndTimestamp = 0;
    }

    internal void ResetAfterReturn()
    {
        Kind = JobDescriptorKind.Empty;
        DependencyCount = 0;
        RemainingDependencies = 0;
        StartIndex = 0;
        EndIndex = 0;
        BatchIndex = 0;
        BatchCount = 0;
        PayloadByteOffset = 0;
        PayloadByteLength = 0;
        ExceptionIndex = -1;
        ProfilerStartTimestamp = 0;
        ProfilerEndTimestamp = 0;
    }
}
