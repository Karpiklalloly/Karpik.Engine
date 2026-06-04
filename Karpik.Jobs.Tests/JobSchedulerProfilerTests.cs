using Karpik.Memory;
using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class JobSchedulerProfilerTests
{
    [Fact]
    public void ProfilerHooks_RecordSingleJobLifecycle()
    {
        using NativeResult<int> result = new();
        ProfilerRecorder recorder = new();
        JobProfilerHooks hooks = new()
        {
            Published = recorder.RecordPublished,
            Started = recorder.RecordStarted,
            Completed = recorder.RecordCompleted,
            Failed = recorder.RecordFailed
        };
        using JobScheduler scheduler = new(
            capacity: 1,
            maxPayloadByteLength: 64,
            profilerHooks: hooks);
        WriteResultJob job = new()
        {
            Result = result.AsHandle(),
            Value = 99
        };

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle handle));
        scheduler.Complete(handle);

        Assert.Equal(99, result.Value);
        Assert.Equal(1, recorder.PublishedCount);
        Assert.Equal(1, recorder.StartedCount);
        Assert.Equal(1, recorder.CompletedCount);
        Assert.Equal(0, recorder.FailedCount);
        Assert.Equal(handle, recorder.LastPublished.Handle);
        Assert.Equal(JobDescriptorKind.Single, recorder.LastPublished.Kind);
        Assert.Equal(0, recorder.LastPublished.DependencyCount);
        Assert.True(recorder.LastPublished.Timestamp > 0);
    }

    [Fact]
    public void ProfilerHooks_RecordFailedJob()
    {
        ProfilerRecorder recorder = new();
        JobProfilerHooks hooks = new()
        {
            Published = recorder.RecordPublished,
            Started = recorder.RecordStarted,
            Completed = recorder.RecordCompleted,
            Failed = recorder.RecordFailed
        };
        using JobScheduler scheduler = new(
            capacity: 1,
            maxPayloadByteLength: 64,
            profilerHooks: hooks);
        ThrowJob job = new()
        {
            ErrorCode = 12
        };

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle handle));
        Assert.Throws<InvalidOperationException>(() => scheduler.Complete(handle));

        Assert.Equal(1, recorder.PublishedCount);
        Assert.Equal(1, recorder.StartedCount);
        Assert.Equal(0, recorder.CompletedCount);
        Assert.Equal(1, recorder.FailedCount);
        Assert.Equal(handle, recorder.LastFailed.Handle);
        Assert.Equal(JobDescriptorKind.Single, recorder.LastFailed.Kind);
        Assert.True(recorder.LastFailed.Timestamp > 0);
    }

    [Fact]
    public void TryGetBatchInfo_ReturnsParallelPublicationMetadata()
    {
        using NativeArray<int> values = new(17);
        values.Clear();
        using JobScheduler scheduler = new(capacity: 1, maxPayloadByteLength: 64);
        IncrementSliceJob job = new()
        {
            Values = values.AsSlice()
        };

        Assert.True(scheduler.TryScheduleParallel(in job, values.Length, batchSize: 4, out ValueJobHandle handle));
        Assert.True(scheduler.TryGetBatchInfo(handle, out JobBatchInfo info));

        Assert.Equal(0, info.StartIndex);
        Assert.Equal(17, info.EndIndex);
        Assert.Equal(17, info.Length);
        Assert.Equal(4, info.BatchSize);
        Assert.Equal(5, info.BatchCount);

        scheduler.Complete(handle);

        Assert.False(scheduler.TryGetBatchInfo(handle, out _));
    }

    [Fact]
    public void TryScheduleComplete_WithoutProfilerHooks_AllocatesZeroManagedBytes()
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

    private sealed class ProfilerRecorder
    {
        public int PublishedCount;
        public int StartedCount;
        public int CompletedCount;
        public int FailedCount;
        public JobProfilerEvent LastPublished;
        public JobProfilerEvent LastStarted;
        public JobProfilerEvent LastCompleted;
        public JobProfilerEvent LastFailed;

        public void RecordPublished(in JobProfilerEvent profilerEvent)
        {
            PublishedCount++;
            LastPublished = profilerEvent;
        }

        public void RecordStarted(in JobProfilerEvent profilerEvent)
        {
            StartedCount++;
            LastStarted = profilerEvent;
        }

        public void RecordCompleted(in JobProfilerEvent profilerEvent)
        {
            CompletedCount++;
            LastCompleted = profilerEvent;
        }

        public void RecordFailed(in JobProfilerEvent profilerEvent)
        {
            FailedCount++;
            LastFailed = profilerEvent;
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

    private struct ThrowJob : IJob
    {
        public int ErrorCode;

        public void Execute()
        {
            throw new InvalidOperationException($"Profiler failure {ErrorCode}.");
        }
    }
}
