using System.Diagnostics;
using Karpik.Memory;
using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class JobSchedulerWorkerRuntimeTests
{
    [Fact]
    public void StartWorkers_TryPublish_WakesWorkerAndCompletesJob()
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
            Value = 17
        };

        Assert.True(scheduler.StartWorkers());
        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle handle));
        Assert.True(scheduler.TryPublish(handle, workerIndex: 0));

        WaitUntilCompletedAndReturned(scheduler, handle);

        Assert.Equal(17, result.Value);
        Assert.Equal(0, scheduler.ScheduledCount);

        scheduler.StopWorkers();
    }

    [Fact]
    public void StartWorkers_WhenWorkerQueueIsSkewed_AllowsOtherWorkerToSteal()
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

        Assert.True(scheduler.StartWorkers());
        Assert.True(scheduler.TrySchedule(in firstJob, out ValueJobHandle first));
        Assert.True(scheduler.TrySchedule(in secondJob, out ValueJobHandle second));
        Assert.True(scheduler.TryPublish(first, workerIndex: 0));
        Assert.True(scheduler.TryPublish(second, workerIndex: 0));

        WaitUntilCompleted(scheduler, first);
        WaitUntilCompleted(scheduler, second);

        Assert.Equal(0, scheduler.ScheduledCount);

        scheduler.StopWorkers();
    }

    [Fact]
    public void StopWorkers_PreventsLaterWorkerPublication()
    {
        using JobScheduler scheduler = new(
            capacity: 1,
            maxPayloadByteLength: 64,
            workerCount: 1,
            workerQueueCapacity: 1);
        NoOpJob job = default;

        Assert.True(scheduler.StartWorkers());
        scheduler.StopWorkers();

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle handle));
        Assert.False(scheduler.TryPublish(handle, workerIndex: 0));
        Assert.Equal(1, scheduler.ScheduledCount);
    }

    [Fact]
    public void SchedulePublishWait_WithWorkersRunning_AllocatesZeroManagedBytesOnOrchestrationThread()
    {
        const int iterations = 128;
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

        Assert.True(scheduler.StartWorkers());
        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle warmup));
        Assert.True(scheduler.TryPublish(warmup, workerIndex: 0));
        WaitUntilCompletedAndReturned(scheduler, warmup);

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

            WaitUntilCompletedAndReturned(scheduler, handle);
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.Equal(iterations - 1, result.Value);
        Assert.Equal(0, allocatedBytes);

        scheduler.StopWorkers();
    }

    private static void WaitUntilCompleted(JobScheduler scheduler, ValueJobHandle handle)
    {
        long timeout = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        SpinWait spinWait = default;

        while (!scheduler.IsCompleted(handle))
        {
            if (Stopwatch.GetTimestamp() >= timeout)
            {
                throw new TimeoutException("Timed out waiting for value job completion.");
            }

            spinWait.SpinOnce();
        }
    }

    private static void WaitUntilCompletedAndReturned(JobScheduler scheduler, ValueJobHandle handle)
    {
        long timeout = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        SpinWait spinWait = default;

        while (!scheduler.IsCompleted(handle) || scheduler.ScheduledCount != 0)
        {
            if (Stopwatch.GetTimestamp() >= timeout)
            {
                throw new TimeoutException("Timed out waiting for value job completion and descriptor return.");
            }

            spinWait.SpinOnce();
        }
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
