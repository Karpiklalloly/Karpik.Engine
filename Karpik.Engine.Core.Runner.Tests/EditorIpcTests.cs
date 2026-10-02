using Karpik.Engine.Core;
using System.Reflection;
using Xunit;

public sealed class EditorIpcTests
{
    [Fact]
    public async Task ShutdownRequest_UsesTheMainThreadScheduler()
    {
        var pipeName = $"KarpikShutdownThreadTests_{Guid.NewGuid():N}";
        using var server = new IpcServer(pipeName);
        var callbackThread = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new IpcClient(pipeName)
        {
            OnShutdownRequest = () => callbackThread.TrySetResult(Environment.CurrentManagedThreadId)
        };
        Task connection = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await connection;
        await Task.Run(() =>
        {
            int owner = Environment.CurrentManagedThreadId;
            using var scheduler = new MainThreadScheduler(owner);
            client.SetScheduler(scheduler);
            server.SendShutdownRequestAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.True(SpinWait.SpinUntil(() =>
            {
                scheduler.Execute();
                return callbackThread.Task.IsCompleted;
            }, TimeSpan.FromSeconds(5)));
            Assert.Equal(owner, callbackThread.Task.GetAwaiter().GetResult());
        });
    }

    [Fact]
    public async Task TryRequestStateAsync_ImmediateResponsesAreNotLost()
    {
        var pipeName = $"KarpikStateTests_{Guid.NewGuid():N}";
        using var server = new IpcServer(pipeName);
        using var client = new IpcClient(pipeName)
        {
            OnStateRequest = () => new HotReloadState { Timestamp = 42 }
        };
        Task waitTask = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waitTask;

        for (int index = 0; index < 32; index++)
        {
            (bool received, HotReloadState? state) = await server.TryRequestStateAsync(TimeSpan.FromSeconds(1));
            Assert.True(received);
            Assert.Equal(42, state!.Timestamp);
        }
    }

    [Fact]
    public async Task TryRequestStateAsync_CancellationAfterSendFinishesDestructiveRequestAndRemovesHandler()
    {
        var pipeName = $"KarpikStateCancellationTests_{Guid.NewGuid():N}";
        using var server = new IpcServer(pipeName);
        using var requestStarted = new ManualResetEventSlim();
        using var releaseRequest = new ManualResetEventSlim();
        using var client = new IpcClient(pipeName)
        {
            OnStateRequest = () =>
            {
                requestStarted.Set();
                releaseRequest.Wait(TimeSpan.FromSeconds(5));
                return new HotReloadState();
            }
        };
        Task waitTask = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waitTask;
        int baseline = SubscriberCount(server);
        using var cancellation = new CancellationTokenSource();

        Task request = server.TryRequestStateAsync(TimeSpan.FromSeconds(5), cancellation.Token);
        Assert.True(requestStarted.Wait(TimeSpan.FromSeconds(2)));
        cancellation.Cancel();
        releaseRequest.Set();
        await request;

        Assert.Equal(baseline, SubscriberCount(server));
    }

    [Fact]
    public async Task Dispose_WithStateRequestInFlight_DoesNotReleaseDisposedGate()
    {
        var pipeName = $"KarpikStateDisposeTests_{Guid.NewGuid():N}";
        var server = new IpcServer(pipeName);
        using var requestStarted = new ManualResetEventSlim();
        using var releaseRequest = new ManualResetEventSlim();
        using var client = new IpcClient(pipeName)
        {
            OnStateRequest = () =>
            {
                requestStarted.Set();
                releaseRequest.Wait(TimeSpan.FromSeconds(5));
                return new HotReloadState();
            }
        };
        Task waitTask = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waitTask;
        Task<(bool Received, HotReloadState? State)> request =
            server.TryRequestStateAsync(TimeSpan.FromSeconds(5));
        Assert.True(requestStarted.Wait(TimeSpan.FromSeconds(2)));

        server.Dispose();
        releaseRequest.Set();
        (bool received, _) = await request;

        Assert.False(received);
    }

    [Fact]
    public async Task SendAsync_SerializesConcurrentFrames()
    {
        var pipeName = $"KarpikConcurrentSendTests_{Guid.NewGuid():N}";
        using var server = new IpcServer(pipeName);
        using var client = new IpcClient(pipeName);
        int received = 0;
        var allReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageReceived += message =>
        {
            if (message.Type == IpcMessageType.WorkerReady
                && Interlocked.Increment(ref received) == 64)
            {
                allReceived.TrySetResult();
            }
        };
        Task waitTask = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waitTask;
        byte[] payload = new byte[32 * 1024];

        await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => server.SendAsync(new IpcMessage(IpcMessageType.WorkerReady, payload))));
        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(64, received);
    }

    [Fact]
    public async Task SendShutdownRequestAsync_SendFailureRemovesAckHandler()
    {
        using var server = new IpcServer($"KarpikShutdownFailureTests_{Guid.NewGuid():N}");
        int baseline = SubscriberCount(server);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.SendShutdownRequestAsync(TimeSpan.FromMilliseconds(10)));

        Assert.Equal(baseline, SubscriberCount(server));
    }

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

    private static int SubscriberCount(IpcServer server)
    {
        var handlers = (MulticastDelegate?)typeof(IpcServer)
            .GetField("OnMessageReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(server);
        return handlers?.GetInvocationList().Length ?? 0;
    }
}
