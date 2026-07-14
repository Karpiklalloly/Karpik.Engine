using Karpik.Engine.Core;
using Xunit;

public sealed class EditorIpcTests
{
    [Fact]
    public async Task RequestEditorSnapshotAsync_RoundTripsSnapshotWithoutStoppingClient()
    {
        var pipeName = $"KarpikEditorTests_{Guid.NewGuid():N}";
        using var server = new IpcServer(pipeName);
        using var client = new IpcClient(pipeName)
        {
            OnEditorSnapshotRequest = () => new EditorRuntimeSnapshot
            {
                CapturedAtUnixMilliseconds = 42,
                Entities =
                [
                    new EditorEntitySnapshot
                    {
                        EntityId = 7,
                        Components =
                        [
                            new EditorComponentSnapshot
                            {
                                TypeName = "Position",
                                DisplayValue = "X = 1, Y = 2"
                            }
                        ]
                    }
                ]
            }
        };

        var waitTask = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waitTask;

        var snapshot = await server.RequestEditorSnapshotAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(snapshot);
        Assert.Equal(42, snapshot.CapturedAtUnixMilliseconds);
        Assert.Equal(7, Assert.Single(snapshot.Entities).EntityId);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task RequestEditorSnapshotAsync_SerializesConcurrentRequests()
    {
        var pipeName = $"KarpikEditorTests_{Guid.NewGuid():N}";
        using var server = new IpcServer(pipeName);
        var requestCount = 0;
        using var client = new IpcClient(pipeName)
        {
            OnEditorSnapshotRequest = () => new EditorRuntimeSnapshot
            {
                CapturedAtUnixMilliseconds = Interlocked.Increment(ref requestCount)
            }
        };

        Task waitTask = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waitTask;

        EditorRuntimeSnapshot?[] snapshots = await Task.WhenAll(
            server.RequestEditorSnapshotAsync(TimeSpan.FromSeconds(2)),
            server.RequestEditorSnapshotAsync(TimeSpan.FromSeconds(2)));

        Assert.Equal(2, requestCount);
        Assert.Equal([1L, 2L], snapshots.Select(snapshot => snapshot!.CapturedAtUnixMilliseconds));
    }

    [Fact]
    public async Task RequestEditorSnapshotAsync_WhenCaptureThrows_KeepsIpcLoopAlive()
    {
        var pipeName = $"KarpikEditorTests_{Guid.NewGuid():N}";
        using var server = new IpcServer(pipeName);
        var requestCount = 0;
        using var client = new IpcClient(pipeName)
        {
            OnEditorSnapshotRequest = () => Interlocked.Increment(ref requestCount) == 1
                ? throw new InvalidOperationException("snapshot failed")
                : new EditorRuntimeSnapshot { CapturedAtUnixMilliseconds = 42 }
        };
        Task waitTask = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waitTask;

        EditorRuntimeSnapshot? failedSnapshot = await server.RequestEditorSnapshotAsync(TimeSpan.FromSeconds(1));
        EditorRuntimeSnapshot? nextSnapshot = await server.RequestEditorSnapshotAsync(TimeSpan.FromSeconds(1));

        Assert.Null(failedSnapshot);
        Assert.Equal(42, nextSnapshot!.CapturedAtUnixMilliseconds);
        Assert.True(client.IsConnected);
    }
}
