using Karpik.Memory;
using Xunit;

namespace Karpik.Memory.Tests;

public sealed class NativeAllocationBudgetTests : IDisposable
{
    public void Dispose()
    {
        Assert.Equal(0, NativeMemoryDiagnostics.ActiveAllocationCount);
    }

    [Fact]
    public void NativeArray_PreallocatedTraversalAndClear_AllocatesZeroManagedBytes()
    {
        using NativeArray<int> array = new(1024);

        AllocationBudget.AssertNoManagedAllocations(() =>
        {
            for (int i = 0; i < array.Length; i++)
            {
                array[i]++;
            }

            array.Clear();
        });
    }

    [Fact]
    public void NativeLinearAllocator_PreallocatedAllocateAndReset_AllocatesZeroManagedBytes()
    {
        using NativeLinearAllocator allocator = new(byteCapacity: 4096, alignment: 16);

        AllocationBudget.AssertNoManagedAllocations(() =>
        {
            allocator.Reset();
            for (int i = 0; i < 32; i++)
            {
                NativeMemorySlice slice = allocator.Allocate(64, 16);
                slice.Bytes[0] = (byte)i;
            }
        });
    }

    [Fact]
    public void NativePool_RentReturn_AllocatesZeroManagedBytes()
    {
        using NativePool<int> pool = new(capacity: 64);

        AllocationBudget.AssertNoManagedAllocations(() =>
        {
            for (int i = 0; i < 64; i++)
            {
                NativePoolHandle<int> handle = pool.Rent();
                handle.Value = i;
                pool.Return(handle);
            }
        });
    }

    [Fact]
    public void NativeResult_HandleAccess_AllocatesZeroManagedBytes()
    {
        using NativeResult<int> result = new();
        NativeResultHandle<int> handle = result.AsHandle();

        AllocationBudget.AssertNoManagedAllocations(() =>
        {
            for (int i = 0; i < 1024; i++)
            {
                handle.Value = i;
            }
        });
    }
}
