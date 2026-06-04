namespace Karpik.Jobs;

public readonly struct JobBatchInfo
{
    internal JobBatchInfo(int startIndex, int endIndex, int batchSize, int batchCount)
    {
        StartIndex = startIndex;
        EndIndex = endIndex;
        BatchSize = batchSize;
        BatchCount = batchCount;
    }

    public int StartIndex { get; }
    public int EndIndex { get; }
    public int Length => EndIndex - StartIndex;
    public int BatchSize { get; }
    public int BatchCount { get; }
}
