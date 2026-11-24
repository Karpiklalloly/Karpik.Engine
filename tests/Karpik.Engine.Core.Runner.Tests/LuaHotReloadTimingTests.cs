using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class LuaHotReloadTimingTests
{
    [Fact]
    public void ClientTicker_ExcludesMaintenanceAndKeepsFractionalRemainder()
    {
        var clock = new ManualClock();
        int ticks = 0;
        var ticker = new FixedRunTicker(() => ticks++, Side.Client, NullLogger<FixedRunTicker>.Instance, clock);
        ticker.FixedRun();
        Assert.Equal(1, ticks);
        clock.AdvanceMilliseconds(13);
        ticker.PauseForMaintenance();
        clock.AdvanceMilliseconds(100_000);
        ticker.ResumeAfterMaintenance();
        ticker.FixedRun();
        Assert.Equal(1, ticks);
        clock.AdvanceMilliseconds(6);
        ticker.FixedRun();
        Assert.Equal(1, ticks);
        clock.AdvanceMilliseconds(1);
        ticker.FixedRun();
        Assert.Equal(2, ticks);
    }

    [Fact]
    public void ClientTicker_RepeatedPauseResume_DoesNotChangeExistingDeadline()
    {
        var clock = new ManualClock();
        int ticks = 0;
        var ticker = new FixedRunTicker(() => ticks++, Side.Client, NullLogger<FixedRunTicker>.Instance, clock);
        ticker.FixedRun();
        for (int i = 0; i < 20; i++)
        {
            clock.AdvanceMilliseconds(1);
            ticker.PauseForMaintenance();
            ticker.PauseForMaintenance();
            clock.AdvanceMilliseconds(1000);
            ticker.ResumeAfterMaintenance();
            ticker.ResumeAfterMaintenance();
        }
        ticker.FixedRun();
        Assert.Equal(2, ticks);
    }

    [Fact]
    public void ServerTicker_Maintenance_DoesNotCreateExtraTicks()
    {
        var clock = new ManualClock();
        int ticks = 0;
        var ticker = new FixedRunTicker(() => ticks++, Side.Server, NullLogger<FixedRunTicker>.Instance, clock);
        ticker.FixedRun();
        ticker.PauseForMaintenance();
        clock.AdvanceMilliseconds(100_000);
        ticker.ResumeAfterMaintenance();
        ticker.FixedRun();
        Assert.Equal(2, ticks);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;
        public void AdvanceMilliseconds(int milliseconds) => _timestamp += milliseconds;
    }
}
