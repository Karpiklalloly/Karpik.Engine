using Karpik.Memory;
using Xunit;

namespace Karpik.Memory.Tests;

public sealed class NativeArrayTests : IDisposable
{
    public void Dispose()
    {
        Assert.Equal(0, NativeMemoryDiagnostics.ActiveAllocationCount);
    }

    [Fact]
    public void Constructor_NegativeLength_Throws()
    {
        AssertThrows<ArgumentOutOfRangeException>(() => new NativeArray<int>(-1));
    }

    [Fact]
    public void Constructor_ZeroLength_DoesNotAllocateAndExposesEmptySpan()
    {
        using NativeArray<int> array = new(0);

        Assert.Equal(0, array.Length);
        Assert.Equal(0, array.Span.Length);
        Assert.Equal(0, array.ReadOnlySpan.Length);
        Assert.Equal(0, array.AsSlice().Length);
        Assert.Equal(0, NativeMemoryDiagnostics.ActiveAllocationCount);
    }

    [Fact]
    public void Indexer_ReturnsRefToDenseStorage()
    {
        using NativeArray<int> array = new(3);

        array[0] = 10;
        array[1] = 20;
        array[2] = 30;
        ref int value = ref array[1];
        value = 99;

        Assert.Equal(10, array[0]);
        Assert.Equal(99, array[1]);
        Assert.Equal(30, array[2]);
    }

    [Fact]
    public void Indexer_IndexBelowZeroOrAtLength_Throws()
    {
        using NativeArray<int> array = new(2);

        AssertThrows<IndexOutOfRangeException>(() => _ = array[-1]);
        AssertThrows<IndexOutOfRangeException>(() => _ = array[2]);
    }

    [Fact]
    public void Span_ClearAndReadOnlySpanExposeStorage()
    {
        using NativeArray<int> array = new(3);
        array.Span[0] = 1;
        array.Span[1] = 2;
        array.Span[2] = 3;

        Assert.Equal(3, array.ReadOnlySpan.Length);
        Assert.Equal(2, array.ReadOnlySpan[1]);

        array.Clear();

        Assert.Equal(0, array[0]);
        Assert.Equal(0, array[1]);
        Assert.Equal(0, array[2]);
    }

    [Fact]
    public void AccessAfterDispose_Throws()
    {
        NativeArray<int> array = new(1);
        array.Dispose();

        AssertThrows<ObjectDisposedException>(() => _ = array[0]);
        AssertThrows<ObjectDisposedException>(() => _ = array.Span.Length);
        AssertThrows<ObjectDisposedException>(() => _ = array.AsSlice());
    }

    [Fact]
    public void DisposeTwice_Throws()
    {
        NativeArray<int> array = new(1);
        array.Dispose();

        AssertThrows<ObjectDisposedException>(() => array.Dispose());
    }

    [Fact]
    public void CopiedSliceAfterOwnerDispose_ThrowsOnAccess()
    {
        NativeArray<int> array = new(1);
        NativeSlice<int> slice = array.AsSlice();
        array.Dispose();

#if DEBUG
        AssertThrows<ObjectDisposedException>(() => _ = slice[0]);
        AssertThrows<ObjectDisposedException>(() => _ = slice.Span.Length);
#endif
    }

    [Fact]
    public void Slice_IndexerReturnsRefAndChecksBounds()
    {
        using NativeArray<int> array = new(2);
        NativeSlice<int> slice = array.AsSlice();

        slice[0] = 4;
        ref int value = ref slice[1];
        value = 7;

        Assert.Equal(4, array[0]);
        Assert.Equal(7, array[1]);
        AssertThrows<IndexOutOfRangeException>(() => _ = slice[-1]);
        AssertThrows<IndexOutOfRangeException>(() => _ = slice[2]);
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        Exception exception = Assert.Throws<TException>(action);
        Assert.IsType<TException>(exception);
    }
}
