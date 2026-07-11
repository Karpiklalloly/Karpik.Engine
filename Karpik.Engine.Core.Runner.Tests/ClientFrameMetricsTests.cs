using Karpik.Engine.Core;
using Xunit;

public sealed class ClientFrameMetricsTests
{
    [Fact]
    public void FrameTimingWindow_AtIntervalBoundary_PublishesAverageP95AndMaximum()
    {
        var window = new FrameTimingWindow(windowDurationTicks: 100);

        window.Publish(10, timestamp: 0);
        window.Publish(30, timestamp: 10);
        window.Publish(20, timestamp: 20);
        window.Publish(5, timestamp: 100);

        TimingSummary summary = window.GetSummary();

        Assert.Equal(3, summary.SampleCount);
        Assert.Equal(20, summary.AverageTicks);
        Assert.Equal(30, summary.P95Ticks);
        Assert.Equal(30, summary.MaxTicks);
    }

    [Fact]
    public void FrameCounterWindow_AtIntervalBoundary_PublishesUnavailableCount()
    {
        var window = new FrameCounterWindow(windowDurationTicks: 100);

        window.Publish(isAvailable: false, timestamp: 0);
        window.Publish(isAvailable: true, timestamp: 10);
        window.Publish(isAvailable: false, timestamp: 20);
        window.Publish(isAvailable: true, timestamp: 100);

        Assert.Equal(2, window.UnavailableCount);
    }

}
