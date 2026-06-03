namespace Karpik.Memory;

public sealed unsafe class NativeArena : IDisposable
{
    private readonly nuint _blockByteLength;
    private readonly nuint _alignment;
    private Block? _head;
    private Block? _current;
    private bool _isDisposed;

    public NativeArena(int blockByteLength, int alignment)
    {
        if (blockByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blockByteLength), blockByteLength, "Block byte length must be greater than zero.");
        }

        if (alignment <= 0 || ((uint)alignment & ((uint)alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Alignment must be a non-zero power of two.");
        }

        _blockByteLength = (nuint)blockByteLength;
        _alignment = (nuint)alignment;
    }

    public int BlockCount { get; private set; }

    public NativeMemorySlice Allocate(nuint byteLength, nuint alignment)
    {
        EnsureNotDisposed();
        ValidateRequest(byteLength, alignment);

        Block block = _current is not null && CanFit(_current, byteLength, alignment)
            ? _current
            : AddBlock(byteLength, alignment);

        nuint offset = NativeMemoryMath.AlignUp(block.UsedBytes, alignment);
        block.UsedBytes = checked(offset + byteLength);
        return new NativeMemorySlice(
            block.Pointer + offset,
            byteLength,
            block.Allocation.Token);
    }

    public void Reset()
    {
        EnsureNotDisposed();
        DisposeBlocks();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NativeArena));
        }

        _isDisposed = true;
        DisposeBlocks();
    }

    private Block AddBlock(nuint byteLength, nuint requestedAlignment)
    {
        nuint alignment = _alignment > requestedAlignment ? _alignment : requestedAlignment;
        nuint minimumLength = NativeMemoryMath.AlignUp(byteLength, alignment);
        nuint blockLength = _blockByteLength > minimumLength ? _blockByteLength : minimumLength;
        NativeAllocation allocation = NativeAllocation.Allocate(blockLength, alignment, NativeAllocationKind.Arena);
        Block block = new(allocation);

        if (_head is null)
        {
            _head = block;
        }
        else
        {
            _current!.Next = block;
        }

        _current = block;
        BlockCount++;
        return block;
    }

    private static bool CanFit(Block block, nuint byteLength, nuint alignment)
    {
        nuint offset = NativeMemoryMath.AlignUp(block.UsedBytes, alignment);
        return checked(offset + byteLength) <= block.Allocation.ByteLength;
    }

    private void DisposeBlocks()
    {
        Block? block = _head;
        while (block is not null)
        {
            Block? next = block.Next;
            block.Allocation.Dispose();
            block = next;
        }

        _head = null;
        _current = null;
        BlockCount = 0;
    }

    private void EnsureNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NativeArena));
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

    private sealed class Block
    {
        public readonly NativeAllocation Allocation;
        public readonly byte* Pointer;
        public nuint UsedBytes;
        public Block? Next;

        public Block(NativeAllocation allocation)
        {
            Allocation = allocation;
            Pointer = (byte*)allocation.Pointer;
        }
    }
}
