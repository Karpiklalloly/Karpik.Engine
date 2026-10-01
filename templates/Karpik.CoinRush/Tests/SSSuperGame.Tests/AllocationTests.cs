using SSSuperGame.Shared.CoinRush;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks that fixed-tick gameplay helpers allocate no managed memory.</summary>
public sealed class AllocationTests
{
    /// <summary>Verifies that fixed-tick helpers allocate no managed memory.</summary>
    [Fact]
    public void Fixed_tick_helpers_allocate_nothing()
    {
        var mv = new MoveState();
        for (int i = 0; i < 100; i++)
        {
            MatchRules.UpdateJump(ref mv, grounded: true, jumpPressed: false, dt: 1f / 60f);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20000; i++)
        {
            MatchRules.UpdateJump(ref mv, grounded: (i % 2) == 0, jumpPressed: (i % 7) == 0, dt: 1f / 60f);
            MatchRules.TargetSpeed(0.5f, true);
            MatchRules.Lerp(0f, 1f, 0.5f);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(before, after);
    }
}
