using Karpik.Memory;
using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class JobSchedulerValueSchedulingTests
{
    [Fact]
    public void TrySchedule_CompleteExecutesStoredUnmanagedPayloadOnce()
    {
        using NativeResult<int> result = new();
        using JobScheduler scheduler = new(capacity: 4, maxPayloadByteLength: 64);
        WriteResultJob job = new()
        {
            Result = result.AsHandle(),
            Value = 123
        };

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle handle));
        Assert.True(handle.IsValid);
        Assert.Equal(1, scheduler.ScheduledCount);
        Assert.Equal(3, scheduler.AvailableDescriptorCount);

        scheduler.Complete(handle);

        Assert.Equal(123, result.Value);
        Assert.Equal(0, scheduler.ScheduledCount);
        Assert.Equal(4, scheduler.AvailableDescriptorCount);
        Assert.Throws<InvalidOperationException>(() => scheduler.Complete(handle));
    }

    [Fact]
    public void TryScheduleParallel_CompleteExecutesEveryIndex()
    {
        using NativeArray<int> values = new(17);
        values.Clear();
        using JobScheduler scheduler = new(capacity: 2, maxPayloadByteLength: 64);
        IncrementSliceJob job = new()
        {
            Values = values.AsSlice()
        };

        Assert.True(scheduler.TryScheduleParallel(in job, values.Length, batchSize: 4, out ValueJobHandle handle));

        scheduler.Complete(handle);

        for (int i = 0; i < values.Length; i++)
        {
            Assert.Equal(1, values[i]);
        }
    }

    [Fact]
    public void TrySchedule_WhenDescriptorCapacityExhausted_ReturnsFalse()
    {
        using JobScheduler scheduler = new(capacity: 1, maxPayloadByteLength: 64);
        NoOpJob job = default;

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle first));
        Assert.False(scheduler.TrySchedule(in job, out ValueJobHandle exhausted));

        Assert.True(first.IsValid);
        Assert.False(exhausted.IsValid);
        Assert.Equal(1, scheduler.ScheduledCount);
        Assert.Equal(0, scheduler.AvailableDescriptorCount);
    }

    [Fact]
    public void TrySchedule_WhenPayloadTooLarge_ReturnsFalseWithoutRentingDescriptor()
    {
        using JobScheduler scheduler = new(capacity: 1, maxPayloadByteLength: 8);
        LargeJob job = default;

        Assert.False(scheduler.TrySchedule(in job, out ValueJobHandle handle));

        Assert.False(handle.IsValid);
        Assert.Equal(0, scheduler.ScheduledCount);
        Assert.Equal(1, scheduler.AvailableDescriptorCount);
    }

    [Fact]
    public void TryScheduleComplete_SteadyState_AllocatesZeroManagedBytes()
    {
        const int iterations = 1_000;
        using NativeResult<int> result = new();
        using JobScheduler scheduler = new(capacity: 1, maxPayloadByteLength: 64);
        WriteResultJob job = new()
        {
            Result = result.AsHandle(),
            Value = 1
        };

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle warmup));
        scheduler.Complete(warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < iterations; i++)
        {
            job.Value = i;
            if (!scheduler.TrySchedule(in job, out ValueJobHandle handle))
            {
                throw new InvalidOperationException("Value job scheduling unexpectedly failed.");
            }

            scheduler.Complete(handle);
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.Equal(iterations - 1, result.Value);
        Assert.Equal(0, allocatedBytes);
    }

    private struct NoOpJob : IJob
    {
        public void Execute()
        {
        }
    }

    private struct WriteResultJob : IJob
    {
        public NativeResultHandle<int> Result;
        public int Value;

        public void Execute()
        {
            Result.Value = Value;
        }
    }

    private struct IncrementSliceJob : IJobFor
    {
        public NativeSlice<int> Values;

        public void Execute(int index)
        {
            Values[index]++;
        }
    }

    private struct LargeJob : IJob
    {
        public long A;
        public long B;

        public void Execute()
        {
            A++;
            B++;
        }
    }
}
