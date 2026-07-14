using Karpik.Engine.Core;
using Xunit;

public sealed class EditorPreviewControllerTests
{
    [Theory]
    [InlineData(Side.Client)]
    [InlineData(Side.Server)]
    public void Constructor_PreservesSideAndHasNoProcessIdBeforeStart(Side side)
    {
        using var controller = new EditorPreviewController(side, "unused-worker-path");

        Assert.Equal(side, controller.Side);
        Assert.Null(controller.ProcessId);
    }

    [Fact]
    public async Task StartAsync_MissingWorker_TransitionsToFaulted()
    {
        using var controller = new EditorPreviewController(
            Side.Client,
            Path.Combine(Path.GetTempPath(), $"missing-worker-{Guid.NewGuid():N}.exe"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => controller.StartAsync());

        Assert.Equal(EditorPreviewState.Faulted, controller.State);
    }

    [Fact]
    public async Task StopAsync_WhenStopped_RemainsStopped()
    {
        using var controller = new EditorPreviewController(Side.Server, "unused-worker-path");

        await controller.StopAsync();

        Assert.Equal(EditorPreviewState.Stopped, controller.State);
    }

    [Theory]
    [InlineData(EditorPreviewState.Starting, true, true)]
    [InlineData(EditorPreviewState.Starting, false, false)]
    [InlineData(EditorPreviewState.Stopped, true, false)]
    [InlineData(EditorPreviewState.Faulted, true, false)]
    public void CanPublishRunning_RequiresSameWorkerToStillBeStartingAndAlive(
        EditorPreviewState state,
        bool workerRunning,
        bool expected)
    {
        Assert.Equal(expected, EditorPreviewController.CanPublishRunning(state, workerRunning));
    }
}
