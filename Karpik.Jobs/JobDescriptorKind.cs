namespace Karpik.Jobs;

public enum JobDescriptorKind : byte
{
    Empty = 0,
    Single = 1,
    ParallelBatch = 2
}
