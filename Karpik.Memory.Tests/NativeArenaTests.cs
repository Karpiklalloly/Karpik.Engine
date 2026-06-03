using Karpik.Memory;
using Xunit;

namespace Karpik.Memory.Tests;

public sealed unsafe class NativeArenaTests : IDisposable
{
    public void Dispose()
    {
        Assert.Equal(0, NativeMemoryDiagnostics.ActiveAllocationCount);
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(-1, 16)]
    [InlineData(64, 0)]
    [InlineData(64, 3)]
    public void Constructor_InvalidBlockSizeOrAlignment_Throws(int blockByteLength, int alignment)
    {
        AssertThrows<ArgumentOutOfRangeException>(() => new NativeArena(blockByteLength, alignment));
    }

    [Fact]
    public void Allocate_ReturnsAlignedSlicesFromCurrentBlock()
    {
        using NativeArena arena = new(blockByteLength: 128, alignment: 16);

        NativeMemorySlice first = arena.Allocate(byteLength: 3, alignment: 1);
        NativeMemorySlice second = arena.Allocate(byteLength: 16, alignment: 16);

        Assert.Equal((nuint)3, first.ByteLength);
        Assert.Equal((nuint)16, second.ByteLength);
        Assert.Equal(1, arena.BlockCount);
        Assert.Equal(1, NativeMemoryDiagnostics.ActiveAllocationCount);
        Assert.Equal((nuint)0, ((nuint)second.Pointer) & 15);
    }

    [Fact]
    public void Allocate_GrowsWithNewBlockWhenCurrentBlockCannotFit()
    {
        using NativeArena arena = new(blockByteLength: 16, alignment: 16);

        NativeMemorySlice first = arena.Allocate(byteLength: 16, alignment: 16);
        NativeMemorySlice second = arena.Allocate(byteLength: 24, alignment: 8);

        first.Bytes[0] = 1;
        second.Bytes[0] = 2;

        Assert.Equal(2, arena.BlockCount);
        Assert.Equal(2, NativeMemoryDiagnostics.ActiveAllocationCount);
        Assert.Equal(1, first.Bytes[0]);
        Assert.Equal(2, second.Bytes[0]);
    }

    [Fact]
    public void Allocate_ZeroLengthOrInvalidAlignment_Throws()
    {
        using NativeArena arena = new(blockByteLength: 64, alignment: 16);

        AssertThrows<ArgumentOutOfRangeException>(() => arena.Allocate(0, 1));
        AssertThrows<ArgumentOutOfRangeException>(() => arena.Allocate(8, 0));
        AssertThrows<ArgumentOutOfRangeException>(() => arena.Allocate(8, 3));
    }

    [Fact]
    public void Reset_ReleasesBlocksAndInvalidatesSlices()
    {
        using NativeArena arena = new(blockByteLength: 16, alignment: 16);
        NativeMemorySlice first = arena.Allocate(byteLength: 16, alignment: 16);
        arena.Allocate(byteLength: 24, alignment: 8);

        arena.Reset();

        Assert.Equal(0, arena.BlockCount);
        Assert.Equal(0, NativeMemoryDiagnostics.ActiveAllocationCount);
#if DEBUG
        AssertThrows<ObjectDisposedException>(() => _ = first.Bytes.Length);
#endif
    }

    [Fact]
    public void Dispose_InvalidatesSlicesAndOperations()
    {
        NativeArena arena = new(blockByteLength: 32, alignment: 16);
        NativeMemorySlice slice = arena.Allocate(8, 8);

        arena.Dispose();

#if DEBUG
        AssertThrows<ObjectDisposedException>(() => _ = slice.Bytes.Length);
#endif
        AssertThrows<ObjectDisposedException>(() => arena.Allocate(8, 8));
        AssertThrows<ObjectDisposedException>(() => arena.Reset());
        AssertThrows<ObjectDisposedException>(() => arena.Dispose());
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        Exception exception = Assert.Throws<TException>(action);
        Assert.IsType<TException>(exception);
    }
}
