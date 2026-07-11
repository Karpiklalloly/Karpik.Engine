using Karpik.Engine.Core;
using Xunit;

public sealed class ClientFrameMetricsTests
{
    [Fact]
    public void Snapshot_ReturnsLastPublishedDurationsAndMergeAvailability()
    {
        var metrics = new ClientFrameMetrics();

        metrics.PublishMainThreadFrame(11);
        metrics.PublishMainThreadBegin(12);
        metrics.PublishMainThreadFrameBegin(13);
        metrics.PublishRender(14);
        metrics.PublishSimulation(15, 16);
        metrics.PublishMergeBuild(17);
        metrics.PublishMergeAvailability(isReady: false);

        ClientFrameTimingSnapshot snapshot = metrics.GetSnapshot();

        Assert.Equal(11, snapshot.MainThreadFrameTicks);
        Assert.Equal(12, snapshot.MainThreadBeginTicks);
        Assert.Equal(13, snapshot.MainThreadFrameBeginTicks);
        Assert.Equal(14, snapshot.RenderTicks);
        Assert.Equal(15, snapshot.SimulationQueueTicks);
        Assert.Equal(16, snapshot.SimulationTicks);
        Assert.Equal(17, snapshot.MergeBuildTicks);
        Assert.False(snapshot.HasCompletedMerge);
    }
}
