using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class JobSystemBaselineTests
{
    [Fact]
    public void Enqueue_IndependentJobs_ExecuteExactlyOnce()
    {
        JobSystem jobs = new(workerCount: 2, prefix: nameof(Enqueue_IndependentJobs_ExecuteExactlyOnce));
        int executed = 0;

        try
        {
            for (int i = 0; i < 128; i++)
            {
                jobs.Enqueue(() => Interlocked.Increment(ref executed));
            }

            jobs.WaitForCompletion();

            Assert.Equal(128, executed);
        }
        finally
        {
            jobs.Shutdown();
        }
    }

    [Fact]
    public void Enqueue_DependencyChain_PreservesOrder()
    {
        JobSystem jobs = new(workerCount: 2, prefix: nameof(Enqueue_DependencyChain_PreservesOrder));
        int stage = 0;

        try
        {
            JobHandle first = jobs.Enqueue(() => Volatile.Write(ref stage, 1));
            JobHandle second = jobs.Enqueue(
                () =>
                {
                    Assert.Equal(1, Volatile.Read(ref stage));
                    Volatile.Write(ref stage, 2);
                },
                first);
            JobHandle third = jobs.Enqueue(
                () =>
                {
                    Assert.Equal(2, Volatile.Read(ref stage));
                    Volatile.Write(ref stage, 3);
                },
                second);

            third.GetAwaiter().GetResult();
            jobs.WaitForCompletion();

            Assert.Equal(3, Volatile.Read(ref stage));
        }
        finally
        {
            jobs.Shutdown();
        }
    }

    [Fact]
    public void Enqueue_FanOutAndFanIn_CompleteBeforeDependentJob()
    {
        JobSystem jobs = new(workerCount: 4, prefix: nameof(Enqueue_FanOutAndFanIn_CompleteBeforeDependentJob));
        int rootCompleted = 0;
        int childrenCompleted = 0;
        int finalObservedChildren = 0;

        try
        {
            JobHandle root = jobs.Enqueue(() => Volatile.Write(ref rootCompleted, 1));
            JobHandle childA = jobs.Enqueue(
                () =>
                {
                    Assert.Equal(1, Volatile.Read(ref rootCompleted));
                    Interlocked.Increment(ref childrenCompleted);
                },
                root);
            JobHandle childB = jobs.Enqueue(
                () =>
                {
                    Assert.Equal(1, Volatile.Read(ref rootCompleted));
                    Interlocked.Increment(ref childrenCompleted);
                },
                root);
            JobHandle final = jobs.Enqueue(
                () =>
                {
                    finalObservedChildren = Volatile.Read(ref childrenCompleted);
                },
                childA,
                childB);

            final.GetAwaiter().GetResult();
            jobs.WaitForCompletion();

            Assert.Equal(2, finalObservedChildren);
        }
        finally
        {
            jobs.Shutdown();
        }
    }

    [Fact]
    public void EnqueueParallel_CoversEveryIndexExactlyOnce()
    {
        JobSystem jobs = new(workerCount: 4, prefix: nameof(EnqueueParallel_CoversEveryIndexExactlyOnce));
        const int itemCount = 257;
        int[] visits = new int[itemCount];

        try
        {
            JobHandle handle = jobs.EnqueueParallel(
                index => Interlocked.Increment(ref visits[index]),
                itemCount,
                batchSize: 17);

            handle.GetAwaiter().GetResult();
            jobs.WaitForCompletion();

            for (int i = 0; i < visits.Length; i++)
            {
                Assert.Equal(1, visits[i]);
            }
        }
        finally
        {
            jobs.Shutdown();
        }
    }

    [Fact]
    public void Enqueue_Exception_IsReportedThroughAwaiter()
    {
        JobSystem jobs = new(workerCount: 1, prefix: nameof(Enqueue_Exception_IsReportedThroughAwaiter));
        InvalidOperationException expected = new("baseline failure");

        try
        {
            JobHandle handle = jobs.Enqueue(() => throw expected);

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
                () => handle.GetAwaiter().GetResult());
            jobs.WaitForCompletion();

            Assert.Same(expected, actual);
        }
        finally
        {
            jobs.Shutdown();
        }
    }

    [Fact]
    public void Shutdown_RejectsNewPublicationWithCompletedDefaultHandle()
    {
        JobSystem jobs = new(workerCount: 1, prefix: nameof(Shutdown_RejectsNewPublicationWithCompletedDefaultHandle));
        int executed = 0;

        jobs.Shutdown();
        JobHandle handle = jobs.Enqueue(() =>
        {
            Interlocked.Increment(ref executed);
        });

        Assert.True(handle.IsCompleted);
        Assert.Equal(0, executed);
    }
}
