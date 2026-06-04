using Karpik.Memory;
using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class JobSchedulerDependencyTests
{
    [Fact]
    public void TryComplete_WhenDependencyIsPending_DoesNotExecuteDependentJob()
    {
        using NativeResult<int> result = new();
        result.Value = 0;
        using JobScheduler scheduler = new(capacity: 2, maxPayloadByteLength: 64);
        WriteResultJob rootJob = new()
        {
            Result = result.AsHandle(),
            Value = 1
        };
        WriteResultJob dependentJob = new()
        {
            Result = result.AsHandle(),
            Value = 2
        };

        Assert.True(scheduler.TrySchedule(in rootJob, out ValueJobHandle root));
        Assert.True(scheduler.TrySchedule(in dependentJob, stackalloc[] { root }, out ValueJobHandle dependent));

        Assert.False(scheduler.TryComplete(dependent));
        Assert.Equal(0, result.Value);
        Assert.False(scheduler.IsCompleted(dependent));

        scheduler.Complete(root);
        Assert.True(scheduler.TryComplete(dependent));

        Assert.Equal(2, result.Value);
        Assert.True(scheduler.IsCompleted(root));
        Assert.True(scheduler.IsCompleted(dependent));
        Assert.Equal(0, scheduler.ScheduledCount);
    }

    [Fact]
    public void TrySchedule_WithCompletedDependency_DoesNotHoldOldDescriptorSlot()
    {
        using NativeResult<int> result = new();
        using JobScheduler scheduler = new(capacity: 1, maxPayloadByteLength: 64);
        WriteResultJob rootJob = new()
        {
            Result = result.AsHandle(),
            Value = 10
        };
        WriteResultJob dependentJob = new()
        {
            Result = result.AsHandle(),
            Value = 20
        };

        Assert.True(scheduler.TrySchedule(in rootJob, out ValueJobHandle root));
        scheduler.Complete(root);

        Assert.True(scheduler.TrySchedule(in dependentJob, stackalloc[] { root }, out ValueJobHandle dependent));
        scheduler.Complete(dependent);

        Assert.Equal(20, result.Value);
        Assert.Equal(1, scheduler.AvailableDescriptorCount);
    }

    [Fact]
    public void TrySchedule_WhenDependencyCapacityExceeded_ReturnsFalseWithoutRentingDescriptor()
    {
        using JobScheduler scheduler = new(capacity: 2, maxPayloadByteLength: 64, maxDependenciesPerJob: 1);
        NoOpJob job = default;

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle first));
        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle second));

        Assert.False(scheduler.TrySchedule(in job, stackalloc[] { first, second }, out ValueJobHandle exhausted));

        Assert.False(exhausted.IsValid);
        Assert.Equal(2, scheduler.ScheduledCount);
        Assert.Equal(0, scheduler.AvailableDescriptorCount);
    }

    [Fact]
    public void Complete_WhenJobThrows_RecordsExceptionAndReturnsDescriptor()
    {
        using JobScheduler scheduler = new(capacity: 1, maxPayloadByteLength: 64);
        ThrowJob job = new()
        {
            ErrorCode = 7
        };

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle handle));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => scheduler.Complete(handle));

        Assert.Contains("7", exception.Message);
        Assert.Equal(0, scheduler.ScheduledCount);
        Assert.Equal(1, scheduler.AvailableDescriptorCount);
        Assert.True(scheduler.IsCompleted(handle));
        Assert.True(scheduler.HasException(handle));
        Assert.Same(exception, scheduler.GetException(handle));
    }

    [Fact]
    public void TryScheduleWithDependency_SteadyState_AllocatesZeroManagedBytes()
    {
        const int iterations = 1_000;
        using NativeResult<int> result = new();
        using JobScheduler scheduler = new(capacity: 2, maxPayloadByteLength: 64);
        WriteResultJob rootJob = new()
        {
            Result = result.AsHandle(),
            Value = 1
        };
        WriteResultJob dependentJob = new()
        {
            Result = result.AsHandle(),
            Value = 2
        };

        Assert.True(scheduler.TrySchedule(in rootJob, out ValueJobHandle warmupRoot));
        Assert.True(scheduler.TrySchedule(in dependentJob, stackalloc[] { warmupRoot }, out ValueJobHandle warmupDependent));
        scheduler.Complete(warmupRoot);
        scheduler.Complete(warmupDependent);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < iterations; i++)
        {
            rootJob.Value = i;
            dependentJob.Value = i + 1;

            if (!scheduler.TrySchedule(in rootJob, out ValueJobHandle root))
            {
                throw new InvalidOperationException("Root scheduling unexpectedly failed.");
            }

            if (!scheduler.TrySchedule(in dependentJob, stackalloc[] { root }, out ValueJobHandle dependent))
            {
                throw new InvalidOperationException("Dependent scheduling unexpectedly failed.");
            }

            scheduler.Complete(root);
            scheduler.Complete(dependent);
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.Equal(iterations, result.Value);
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

    private struct ThrowJob : IJob
    {
        public int ErrorCode;

        public void Execute()
        {
            throw new InvalidOperationException($"Value job failed with code {ErrorCode}.");
        }
    }
}
