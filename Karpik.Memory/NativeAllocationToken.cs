namespace Karpik.Memory;

internal readonly struct NativeAllocationToken
{
    internal readonly long Id;
    internal readonly int Version;
    internal readonly int BorrowVersion;

    internal NativeAllocationToken(long id, int version, int borrowVersion = 0)
    {
        Id = id;
        Version = version;
        BorrowVersion = borrowVersion;
    }
}
