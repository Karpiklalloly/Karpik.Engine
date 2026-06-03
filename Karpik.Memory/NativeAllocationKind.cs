namespace Karpik.Memory;

internal enum NativeAllocationKind
{
    Unknown = 0,
    Array = 1,
    Result = 2,
    Arena = 3,
    LinearAllocator = 4,
    Pool = 5
}
