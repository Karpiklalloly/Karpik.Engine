using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class WorkStealingDequeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void Constructor_InvalidCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkStealingDeque<int>(capacity));
    }

    [Fact]
    public void TryPushBottom_TryPopBottom_ReturnsOwnerItemsInLifoOrder()
    {
        using WorkStealingDeque<int> deque = new(capacity: 4);

        Assert.True(deque.TryPushBottom(1));
        Assert.True(deque.TryPushBottom(2));
        Assert.True(deque.TryPushBottom(3));

        Assert.Equal(3, deque.Count);
        Assert.True(deque.TryPopBottom(out int first));
        Assert.True(deque.TryPopBottom(out int second));
        Assert.True(deque.TryPopBottom(out int third));
        Assert.False(deque.TryPopBottom(out _));

        Assert.Equal(3, first);
        Assert.Equal(2, second);
        Assert.Equal(1, third);
        Assert.Equal(0, deque.Count);
    }

    [Fact]
    public void TryStealTop_ReturnsItemsInFifoOrder()
    {
        using WorkStealingDeque<int> deque = new(capacity: 4);

        Assert.True(deque.TryPushBottom(1));
        Assert.True(deque.TryPushBottom(2));
        Assert.True(deque.TryPushBottom(3));

        Assert.True(deque.TryStealTop(out int first));
        Assert.True(deque.TryStealTop(out int second));
        Assert.True(deque.TryPopBottom(out int third));
        Assert.False(deque.TryStealTop(out _));

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, third);
        Assert.Equal(0, deque.Count);
    }

    [Fact]
    public void TryPushBottom_WhenCapacityIsFull_ReturnsFalse()
    {
        using WorkStealingDeque<int> deque = new(capacity: 2);

        Assert.True(deque.TryPushBottom(10));
        Assert.True(deque.TryPushBottom(20));
        Assert.False(deque.TryPushBottom(30));

        Assert.Equal(2, deque.Count);
    }

    [Fact]
    public void PushPopSteal_ReusesSlotsAfterWrapAround()
    {
        using WorkStealingDeque<int> deque = new(capacity: 4);

        Assert.True(deque.TryPushBottom(1));
        Assert.True(deque.TryPushBottom(2));
        Assert.True(deque.TryPushBottom(3));
        Assert.True(deque.TryPushBottom(4));
        Assert.True(deque.TryStealTop(out int stolenA));
        Assert.True(deque.TryStealTop(out int stolenB));
        Assert.True(deque.TryPushBottom(5));
        Assert.True(deque.TryPushBottom(6));
        Assert.False(deque.TryPushBottom(7));

        Assert.True(deque.TryStealTop(out int stolenC));
        Assert.True(deque.TryPopBottom(out int poppedA));
        Assert.True(deque.TryPopBottom(out int poppedB));
        Assert.True(deque.TryPopBottom(out int poppedC));
        Assert.False(deque.TryPopBottom(out _));

        Assert.Equal(1, stolenA);
        Assert.Equal(2, stolenB);
        Assert.Equal(3, stolenC);
        Assert.Equal(6, poppedA);
        Assert.Equal(5, poppedB);
        Assert.Equal(4, poppedC);
    }

    [Fact]
    public void SingleItem_CanBeTakenOnlyOnce()
    {
        using WorkStealingDeque<int> deque = new(capacity: 2);

        Assert.True(deque.TryPushBottom(1));
        Assert.True(deque.TryStealTop(out int stolen));
        Assert.False(deque.TryPopBottom(out _));
        Assert.Equal(1, stolen);

        Assert.True(deque.TryPushBottom(2));
        Assert.True(deque.TryPopBottom(out int popped));
        Assert.False(deque.TryStealTop(out _));
        Assert.Equal(2, popped);
    }

    [Fact]
    public void PushPopSteal_SteadyState_AllocatesZeroManagedBytes()
    {
        const int iterations = 1_000;
        using WorkStealingDeque<int> deque = new(capacity: 64);

        Fill(deque, 64);
        Drain(deque, 64);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            Fill(deque, 64);
            Drain(deque, 64);
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.Equal(0, allocatedBytes);
    }

    private static void Fill(WorkStealingDeque<int> deque, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (!deque.TryPushBottom(i))
            {
                throw new InvalidOperationException("Push unexpectedly failed.");
            }
        }
    }

    private static void Drain(WorkStealingDeque<int> deque, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (!deque.TryPopBottom(out _))
            {
                throw new InvalidOperationException("Pop unexpectedly failed.");
            }
        }
    }
}
