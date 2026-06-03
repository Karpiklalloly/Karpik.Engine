using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Karpik.Memory;

public sealed unsafe class NativeLinearAllocator : IDisposable
{
    private static readonly ConcurrentDictionary<long, int> ActiveBorrowVersions = new();

    private readonly NativeAllocation _allocation;
    private readonly byte* _basePointer;
    private int _borrowVersion;
    private bool _isDisposed;

    public NativeLinearAllocator(int byteCapacity, int alignment)
    {
        if (byteCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCapacity), byteCapacity, "Capacity must be greater than zero.");
        }

        if (alignment <= 0 || ((uint)alignment & ((uint)alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Alignment must be a non-zero power of two.");
        }

        ByteCapacity = (nuint)byteCapacity;
        Alignment = (nuint)alignment;
        _allocation = NativeAllocation.Allocate(ByteCapacity, Alignment, NativeAllocationKind.LinearAllocator);
        _basePointer = (byte*)_allocation.Pointer;
        _borrowVersion = 1;
        ActiveBorrowVersions.TryAdd(_allocation.Id, _borrowVersion);
    }

    public nuint ByteCapacity { get; }
    public nuint Alignment { get; }
    public nuint UsedBytes { get; private set; }

    public NativeMemorySlice Allocate(nuint byteLength, nuint alignment)
    {
        EnsureNotDisposed();
        ValidateRequest(byteLength, alignment);

        nuint alignedOffset = NativeMemoryMath.AlignUp(UsedBytes, alignment);
        nuint nextOffset = checked(alignedOffset + byteLength);
        if (nextOffset > ByteCapacity)
        {
            throw new InvalidOperationException(
                $"Native linear allocator capacity exceeded. Capacity {ByteCapacity}, requested end offset {nextOffset}.");
        }

        UsedBytes = nextOffset;
        return new NativeMemorySlice(
            _basePointer + alignedOffset,
            byteLength,
            new NativeAllocationToken(_allocation.Id, _allocation.Token.Version, _borrowVersion));
    }

    public void Reset()
    {
        EnsureNotDisposed();
        UsedBytes = 0;
        unchecked
        {
            _borrowVersion++;
        }

        ActiveBorrowVersions[_allocation.Id] = _borrowVersion;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NativeLinearAllocator));
        }

        _isDisposed = true;
        ActiveBorrowVersions.TryRemove(_allocation.Id, out _);
        _allocation.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NativeLinearAllocator));
        }
    }

    internal static void ValidateBorrowVersion(NativeAllocationToken token)
    {
        if (!ActiveBorrowVersions.TryGetValue(token.Id, out int version))
        {
            throw new ObjectDisposedException(nameof(NativeLinearAllocator));
        }

        if (version != token.BorrowVersion)
        {
            throw new InvalidOperationException(
                $"Native linear allocator slice is stale. Expected borrow version {version}, got {token.BorrowVersion}.");
        }
    }

    private static void ValidateRequest(nuint byteLength, nuint alignment)
    {
        if (byteLength == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Byte length must be greater than zero.");
        }

        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Alignment must be a non-zero power of two.");
        }
    }
}
