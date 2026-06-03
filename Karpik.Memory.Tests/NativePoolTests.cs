using Karpik.Memory;
using Xunit;

namespace Karpik.Memory.Tests;

public sealed class NativePoolTests : IDisposable
{
    public void Dispose()
    {
        Assert.Equal(0, NativeMemoryDiagnostics.ActiveAllocationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidCapacity_Throws(int capacity)
    {
        AssertThrows<ArgumentOutOfRangeException>(() => new NativePool<int>(capacity));
    }

    [Fact]
    public void Rent_ReturnsRefHandleAndTracksCapacity()
    {
        using NativePool<int> pool = new(capacity: 2);

        NativePoolHandle<int> first = pool.Rent();
        NativePoolHandle<int> second = pool.Rent();
        first.Value = 10;
        second.Value = 20;

        Assert.Equal(2, pool.Capacity);
        Assert.Equal(2, pool.RentedCount);
        Assert.Equal(10, first.Value);
        Assert.Equal(20, second.Value);
        Assert.Equal(1, NativeMemoryDiagnostics.ActiveAllocationCount);
    }

    [Fact]
    public void Rent_WhenCapacityExhausted_ThrowsWithoutChangingRentedCount()
    {
        using NativePool<int> pool = new(capacity: 1);
        pool.Rent();

        AssertThrows<InvalidOperationException>(() => pool.Rent());

        Assert.Equal(1, pool.RentedCount);
    }

    [Fact]
    public void Return_ReusesSlotAndInvalidatesOldHandle()
    {
        using NativePool<int> pool = new(capacity: 1);
        NativePoolHandle<int> first = pool.Rent();
        first.Value = 123;

        pool.Return(first);

        Assert.Equal(0, pool.RentedCount);
        AssertThrows<InvalidOperationException>(() => _ = first.Value);

        NativePoolHandle<int> second = pool.Rent();
        second.Value = 77;

        Assert.Equal(1, pool.RentedCount);
        Assert.Equal(77, second.Value);
    }

    [Fact]
    public void Return_Twice_Throws()
    {
        using NativePool<int> pool = new(capacity: 1);
        NativePoolHandle<int> handle = pool.Rent();
        pool.Return(handle);

        AssertThrows<InvalidOperationException>(() => pool.Return(handle));
    }

    [Fact]
    public void Dispose_InvalidatesHandlesAndOperations()
    {
        NativePool<int> pool = new(capacity: 1);
        NativePoolHandle<int> handle = pool.Rent();

        pool.Dispose();

#if DEBUG
        AssertThrows<ObjectDisposedException>(() => _ = handle.Value);
#endif
        AssertThrows<ObjectDisposedException>(() => pool.Rent());
        AssertThrows<ObjectDisposedException>(() => pool.Return(handle));
        AssertThrows<ObjectDisposedException>(() => pool.Dispose());
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        Exception exception = Assert.Throws<TException>(action);
        Assert.IsType<TException>(exception);
    }
}
