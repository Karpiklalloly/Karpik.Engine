using Karpik.Memory;
using Xunit;

namespace Karpik.Memory.Tests;

public sealed unsafe class NativeAllocationTests : IDisposable
{
    public void Dispose()
    {
        Assert.Equal(0, NativeMemoryDiagnostics.ActiveAllocationCount);
    }

    [Fact]
    public void Allocate_TracksPointerLengthAlignmentAndActiveAllocation()
    {
        using NativeAllocation allocation = NativeAllocation.Allocate(
            byteLength: 64,
            alignment: 16,
            NativeAllocationKind.Array);

        Assert.NotEqual((nint)0, (nint)allocation.Pointer);
        Assert.Equal((nuint)64, allocation.ByteLength);
        Assert.Equal((nuint)16, allocation.Alignment);
        Assert.False(allocation.IsDisposed);
        Assert.Equal(1, NativeMemoryDiagnostics.ActiveAllocationCount);
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(64, 0)]
    [InlineData(64, 3)]
    public void Allocate_InvalidSizeOrAlignment_Throws(ulong byteLength, ulong alignment)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NativeAllocation.Allocate(
                (nuint)byteLength,
                (nuint)alignment,
                NativeAllocationKind.Array));
    }

    [Fact]
    public void Dispose_ReleasesAllocationAndMarksDisposed()
    {
        NativeAllocation allocation = NativeAllocation.Allocate(32, 16, NativeAllocationKind.Result);

        allocation.Dispose();

        Assert.True(allocation.IsDisposed);
        Assert.Equal(0, NativeMemoryDiagnostics.ActiveAllocationCount);
    }

    [Fact]
    public void Dispose_Twice_Throws()
    {
        NativeAllocation allocation = NativeAllocation.Allocate(32, 16, NativeAllocationKind.Array);
        allocation.Dispose();

        AssertThrows<ObjectDisposedException>(() => allocation.Dispose());
    }

    [Fact]
    public void ValidateToken_AfterDispose_Throws()
    {
        NativeAllocation allocation = NativeAllocation.Allocate(32, 16, NativeAllocationKind.Array);
        NativeAllocationToken token = allocation.Token;
        allocation.Dispose();

        AssertThrows<ObjectDisposedException>(() => allocation.Validate(token));
    }

    [Fact]
    public void ValidateToken_ForDifferentAllocation_Throws()
    {
        using NativeAllocation first = NativeAllocation.Allocate(32, 16, NativeAllocationKind.Array);
        using NativeAllocation second = NativeAllocation.Allocate(32, 16, NativeAllocationKind.Array);
        NativeAllocationToken firstToken = first.Token;

        AssertThrows<InvalidOperationException>(() => second.Validate(firstToken));
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        Exception exception = Assert.Throws<TException>(action);
        Assert.IsType<TException>(exception);
    }
}
