namespace Karpik.Jobs;

internal enum JobDescriptorKind : byte
{
    Empty = 0,
    Single = 1,
    ParallelBatch = 2
}
