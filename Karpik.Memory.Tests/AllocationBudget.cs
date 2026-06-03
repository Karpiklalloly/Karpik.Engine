using Xunit;

namespace Karpik.Memory.Tests;

internal static class AllocationBudget
{
    public static void AssertNoManagedAllocations(Action action)
    {
        for (int i = 0; i < 16; i++)
        {
            action();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++)
        {
            action();
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocatedBytes);
    }
}
