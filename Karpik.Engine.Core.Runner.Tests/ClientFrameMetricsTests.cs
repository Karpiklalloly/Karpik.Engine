using System.Diagnostics;
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
        Assert.Equal(30, summary.P99Ticks);
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

        MergeAvailabilitySummary summary = window.GetSummary();
        Assert.Equal(2, summary.UnavailableCount);
        Assert.Equal(3, summary.PollCount);
    }

    [Fact]
    public void FrameTimingWindow_WithMoreSamplesThanPreviousRing_UsesAllSamplesForPercentiles()
    {
        var window = new FrameTimingWindow(windowDurationTicks: 10_000);

        for (int i = 0; i < 1024; i++)
        {
            window.Publish(100, timestamp: i);
        }

        for (int i = 1024; i < 2048; i++)
        {
            window.Publish(10, timestamp: i);
        }

        window.Publish(0, timestamp: 10_000);

        TimingSummary summary = window.GetSummary();

        Assert.Equal(2048, summary.SampleCount);
        Assert.Equal(55, summary.AverageTicks);
        Assert.InRange(summary.P95Ticks, 100, 101);
        Assert.InRange(summary.P99Ticks, 100, 101);
        Assert.Equal(100, summary.MaxTicks);
    }

    [Fact]
    public void NanosecondsToStopwatchTicks_ConvertsOneMillisecond()
    {
        Assert.Equal(Stopwatch.Frequency / 1000, ClientFrameMetrics.NanosecondsToStopwatchTicks(1_000_000));
    }

}
