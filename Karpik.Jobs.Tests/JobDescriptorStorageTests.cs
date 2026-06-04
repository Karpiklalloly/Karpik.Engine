using Karpik.Memory;
using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class JobDescriptorStorageTests
{
    [Fact]
    public void ValueJobContracts_ExecuteUnmanagedJobsWithBorrowedNativeState()
    {
        using NativeResult<int> result = new();
        WriteResultJob job = new()
        {
            Result = result.AsHandle(),
            Value = 42
        };

        Execute(ref job);

        Assert.Equal(42, result.Value);

        using NativeArray<int> values = new(4);
        values.Clear();
        IncrementSliceJob parallelJob = new()
        {
            Values = values.AsSlice()
        };

        ExecuteFor(ref parallelJob, values.Length);

        for (int i = 0; i < values.Length; i++)
        {
            Assert.Equal(1, values[i]);
        }
    }

    [Fact]
    public void JobDescriptorPool_RentInitializesDescriptorAndTracksCapacity()
    {
        using JobDescriptorPool pool = new(capacity: 2);

        bool rented = pool.TryRent(JobDescriptorKind.Single, out JobDescriptorHandle handle);

        Assert.True(rented);
        Assert.True(handle.IsValid);
        Assert.Equal(2, pool.Capacity);
        Assert.Equal(1, pool.RentedCount);
        Assert.Equal(1, pool.AvailableCount);

        ref JobDescriptor descriptor = ref pool.Get(handle);
        Assert.Equal(JobDescriptorKind.Single, descriptor.Kind);
        Assert.Equal(handle.Generation, descriptor.Generation);

        descriptor.DependencyCount = 3;
        descriptor.RemainingDependencies = 2;

        Assert.Equal(3, pool.Get(handle).DependencyCount);
        Assert.Equal(2, pool.Get(handle).RemainingDependencies);
    }

    [Fact]
    public void JobDescriptorPool_ReturnReusesSlotAndInvalidatesOldHandle()
    {
        using JobDescriptorPool pool = new(capacity: 1);
        Assert.True(pool.TryRent(JobDescriptorKind.Single, out JobDescriptorHandle first));

        ref JobDescriptor descriptor = ref pool.Get(first);
        descriptor.DependencyCount = 7;

        Assert.True(pool.TryReturn(first));
        Assert.Equal(0, pool.RentedCount);
        Assert.Equal(1, pool.AvailableCount);
        Assert.False(pool.TryReturn(first));
        Assert.Throws<InvalidOperationException>(() => pool.Get(first));

        Assert.True(pool.TryRent(JobDescriptorKind.ParallelBatch, out JobDescriptorHandle second));

        Assert.Equal(first.Index, second.Index);
        Assert.NotEqual(first.Generation, second.Generation);
        Assert.Equal(JobDescriptorKind.ParallelBatch, pool.Get(second).Kind);
        Assert.Equal(0, pool.Get(second).DependencyCount);
    }

    [Fact]
    public void JobDescriptorPool_TryRent_WhenCapacityExhaustedReturnsFalse()
    {
        using JobDescriptorPool pool = new(capacity: 1);

        Assert.True(pool.TryRent(JobDescriptorKind.Single, out JobDescriptorHandle first));
        Assert.False(pool.TryRent(JobDescriptorKind.Single, out JobDescriptorHandle exhausted));

        Assert.True(first.IsValid);
        Assert.False(exhausted.IsValid);
        Assert.Equal(1, pool.RentedCount);
        Assert.Equal(0, pool.AvailableCount);
    }

    [Fact]
    public void JobDescriptorPool_RentReturnSteadyState_AllocatesZeroManagedBytes()
    {
        const int capacity = 64;
        const int iterations = 1_000;
        using JobDescriptorPool pool = new(capacity);
        JobDescriptorHandle[] handles = new JobDescriptorHandle[capacity];

        RentAll(pool, handles);
        ReturnAll(pool, handles);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            RentAll(pool, handles);
            ReturnAll(pool, handles);
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.Equal(0, allocatedBytes);
    }

    private static void RentAll(JobDescriptorPool pool, JobDescriptorHandle[] handles)
    {
        for (int i = 0; i < handles.Length; i++)
        {
            if (!pool.TryRent(JobDescriptorKind.Single, out handles[i]))
            {
                throw new InvalidOperationException("Descriptor rent unexpectedly failed.");
            }
        }
    }

    private static void ReturnAll(JobDescriptorPool pool, JobDescriptorHandle[] handles)
    {
        for (int i = 0; i < handles.Length; i++)
        {
            if (!pool.TryReturn(handles[i]))
            {
                throw new InvalidOperationException("Descriptor return unexpectedly failed.");
            }
        }
    }

    private static void Execute<TJob>(ref TJob job)
        where TJob : unmanaged, IJob
    {
        job.Execute();
    }

    private static void ExecuteFor<TJob>(ref TJob job, int length)
        where TJob : unmanaged, IJobFor
    {
        for (int i = 0; i < length; i++)
        {
            job.Execute(i);
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
}
