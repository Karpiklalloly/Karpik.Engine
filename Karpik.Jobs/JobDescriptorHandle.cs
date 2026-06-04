namespace Karpik.Jobs;

internal readonly struct JobDescriptorHandle
{
    internal JobDescriptorHandle(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }

    public int Index { get; }
    public int Generation { get; }
    public bool IsValid => Index >= 0 && Generation != 0;
}
