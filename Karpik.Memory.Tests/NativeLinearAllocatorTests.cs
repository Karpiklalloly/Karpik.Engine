using Karpik.Memory;
using Xunit;

namespace Karpik.Memory.Tests;

public sealed unsafe class NativeLinearAllocatorTests : IDisposable
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
    public void Constructor_InvalidCapacityOrAlignment_Throws(int byteCapacity, int alignment)
    {
        AssertThrows<ArgumentOutOfRangeException>(() => new NativeLinearAllocator(byteCapacity, alignment));
    }

    [Fact]
    public void Allocate_ReturnsAlignedSlicesAndAdvancesUsedBytes()
    {
        using NativeLinearAllocator allocator = new(byteCapacity: 128, alignment: 16);

        NativeMemorySlice first = allocator.Allocate(byteLength: 3, alignment: 1);
        NativeMemorySlice second = allocator.Allocate(byteLength: 16, alignment: 16);

        Assert.Equal((nuint)3, first.ByteLength);
        Assert.Equal((nuint)16, second.ByteLength);
        Assert.Equal((nuint)32, allocator.UsedBytes);
        Assert.Equal((nuint)128, allocator.ByteCapacity);
        Assert.Equal(1, NativeMemoryDiagnostics.ActiveAllocationCount);
        Assert.Equal((nuint)0, ((nuint)second.Pointer) & 15);
    }

    [Fact]
    public void Allocate_ZeroLengthOrInvalidAlignment_Throws()
    {
        using NativeLinearAllocator allocator = new(byteCapacity: 64, alignment: 16);

        AssertThrows<ArgumentOutOfRangeException>(() => allocator.Allocate(0, 1));
        AssertThrows<ArgumentOutOfRangeException>(() => allocator.Allocate(8, 0));
        AssertThrows<ArgumentOutOfRangeException>(() => allocator.Allocate(8, 3));
    }

    [Fact]
    public void Allocate_WhenCapacityExceeded_ThrowsWithoutAdvancing()
    {
        using NativeLinearAllocator allocator = new(byteCapacity: 16, alignment: 16);
        NativeMemorySlice first = allocator.Allocate(8, 8);

        AssertThrows<InvalidOperationException>(() => allocator.Allocate(17, 1));

        Assert.Equal((nuint)8, allocator.UsedBytes);
        Assert.Equal((nuint)8, first.ByteLength);
    }

    [Fact]
    public void Reset_AllowsReuseAndInvalidatesOldSlices()
    {
        using NativeLinearAllocator allocator = new(byteCapacity: 32, alignment: 16);
        NativeMemorySlice beforeReset = allocator.Allocate(8, 8);
        beforeReset.Bytes[0] = 123;

        allocator.Reset();

        Assert.Equal((nuint)0, allocator.UsedBytes);
#if DEBUG
        AssertThrows<InvalidOperationException>(() => _ = beforeReset.Bytes.Length);
#endif

        NativeMemorySlice afterReset = allocator.Allocate(8, 8);
        afterReset.Bytes[0] = 77;
        Assert.Equal(77, afterReset.Bytes[0]);
    }

    [Fact]
    public void Dispose_InvalidatesSlicesAndDoubleDisposeThrows()
    {
        NativeLinearAllocator allocator = new(byteCapacity: 32, alignment: 16);
        NativeMemorySlice slice = allocator.Allocate(8, 8);

        allocator.Dispose();

#if DEBUG
        AssertThrows<ObjectDisposedException>(() => _ = slice.Bytes.Length);
#endif
        AssertThrows<ObjectDisposedException>(() => allocator.Allocate(8, 8));
        AssertThrows<ObjectDisposedException>(() => allocator.Reset());
        AssertThrows<ObjectDisposedException>(() => allocator.Dispose());
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        Exception exception = Assert.Throws<TException>(action);
        Assert.IsType<TException>(exception);
    }
}
