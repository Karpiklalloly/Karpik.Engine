using Karpik.Memory;
using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class JobSchedulerWorkerPublicationTests
{
    [Fact]
    public void TryPublish_TryRunNext_ExecutesLocalWorkerJob()
    {
        using NativeResult<int> result = new();
        using JobScheduler scheduler = new(
            capacity: 1,
            maxPayloadByteLength: 64,
            workerCount: 1,
            workerQueueCapacity: 2);
        WriteResultJob job = new()
        {
            Result = result.AsHandle(),
            Value = 11
        };

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle handle));
        Assert.True(scheduler.TryPublish(handle, workerIndex: 0));
        Assert.True(scheduler.TryRunNext(workerIndex: 0));

        Assert.Equal(11, result.Value);
        Assert.True(scheduler.IsCompleted(handle));
        Assert.Equal(0, scheduler.ScheduledCount);
    }

    [Fact]
    public void TryRunNext_WhenLocalQueueIsEmpty_StealsFromOtherWorker()
    {
        using NativeResult<int> result = new();
        using JobScheduler scheduler = new(
            capacity: 2,
            maxPayloadByteLength: 64,
            workerCount: 2,
            workerQueueCapacity: 2);
        WriteResultJob firstJob = new()
        {
            Result = result.AsHandle(),
            Value = 1
        };
        WriteResultJob secondJob = new()
        {
            Result = result.AsHandle(),
            Value = 2
        };

        Assert.True(scheduler.TrySchedule(in firstJob, out ValueJobHandle first));
        Assert.True(scheduler.TrySchedule(in secondJob, out ValueJobHandle second));
        Assert.True(scheduler.TryPublish(first, workerIndex: 0));
        Assert.True(scheduler.TryPublish(second, workerIndex: 0));

        Assert.True(scheduler.TryRunNext(workerIndex: 1));
        Assert.Equal(1, result.Value);
        Assert.True(scheduler.IsCompleted(first));
        Assert.False(scheduler.IsCompleted(second));

        Assert.True(scheduler.TryRunNext(workerIndex: 0));
        Assert.Equal(2, result.Value);
        Assert.True(scheduler.IsCompleted(second));
    }

    [Fact]
    public void TryRunNext_WhenDependenciesArePending_RequeuesWithoutExecuting()
    {
        using NativeResult<int> result = new();
        result.Value = 0;
        using JobScheduler scheduler = new(
            capacity: 2,
            maxPayloadByteLength: 64,
            workerCount: 1,
            workerQueueCapacity: 2);
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
        Assert.True(scheduler.TryPublish(dependent, workerIndex: 0));

        Assert.False(scheduler.TryRunNext(workerIndex: 0));
        Assert.Equal(0, result.Value);
        Assert.False(scheduler.IsCompleted(dependent));

        scheduler.Complete(root);
        Assert.True(scheduler.TryRunNext(workerIndex: 0));

        Assert.Equal(2, result.Value);
        Assert.True(scheduler.IsCompleted(dependent));
    }

    [Fact]
    public void TryRunNext_WhenStolenDependencyIsPending_RequeuesToVictimWorker()
    {
        using NativeResult<int> result = new();
        result.Value = 0;
        using JobScheduler scheduler = new(
            capacity: 2,
            maxPayloadByteLength: 64,
            workerCount: 2,
            workerQueueCapacity: 1);
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
        Assert.True(scheduler.TryPublish(dependent, workerIndex: 0));

        Assert.False(scheduler.TryRunNext(workerIndex: 1));
        Assert.Equal(1, scheduler.GetWorkerQueueCount(workerIndex: 0));
        Assert.Equal(0, scheduler.GetWorkerQueueCount(workerIndex: 1));
        Assert.Equal(0, result.Value);
        Assert.False(scheduler.IsCompleted(dependent));

        scheduler.Complete(root);
        Assert.True(scheduler.TryRunNext(workerIndex: 0));

        Assert.Equal(2, result.Value);
        Assert.True(scheduler.IsCompleted(dependent));
    }

    [Fact]
    public void TryPublish_WhenWorkerQueueIsFull_ReturnsFalse()
    {
        using JobScheduler scheduler = new(
            capacity: 2,
            maxPayloadByteLength: 64,
            workerCount: 1,
            workerQueueCapacity: 1);
        NoOpJob job = default;

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle first));
        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle second));

        Assert.True(scheduler.TryPublish(first, workerIndex: 0));
        Assert.False(scheduler.TryPublish(second, workerIndex: 0));
        Assert.Equal(1, scheduler.GetWorkerQueueCount(workerIndex: 0));
        Assert.Equal(2, scheduler.ScheduledCount);
    }

    [Fact]
    public void PublishRunNext_SteadyState_AllocatesZeroManagedBytes()
    {
        const int iterations = 1_000;
        using NativeResult<int> result = new();
        using JobScheduler scheduler = new(
            capacity: 1,
            maxPayloadByteLength: 64,
            workerCount: 1,
            workerQueueCapacity: 1);
        WriteResultJob job = new()
        {
            Result = result.AsHandle(),
            Value = 1
        };

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle warmup));
        Assert.True(scheduler.TryPublish(warmup, workerIndex: 0));
        Assert.True(scheduler.TryRunNext(workerIndex: 0));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < iterations; i++)
        {
            job.Value = i;
            if (!scheduler.TrySchedule(in job, out ValueJobHandle handle))
            {
                throw new InvalidOperationException("Scheduling unexpectedly failed.");
            }

            if (!scheduler.TryPublish(handle, workerIndex: 0))
            {
                throw new InvalidOperationException("Publish unexpectedly failed.");
            }

            if (!scheduler.TryRunNext(workerIndex: 0))
            {
                throw new InvalidOperationException("Run unexpectedly failed.");
            }
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
}
