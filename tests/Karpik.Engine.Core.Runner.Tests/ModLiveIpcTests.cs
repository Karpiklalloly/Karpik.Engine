using System.IO.Pipes;
using Karpik.Engine.Core;
using Xunit;

public sealed class ModLiveIpcTests
{
    [Fact]
    public async Task LegacyPipeDoesNotDispatchReservedLiveControl()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string name = $"karpik-mod-live-{Guid.NewGuid():N}";
        using var server = new IpcServer(name);
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var observed = new List<IpcMessageType>();
        var sentinel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnMessageReceived += message =>
        {
            observed.Add(message.Type);
            if (message.Type == IpcMessageType.WorkerReady)
                sentinel.TrySetResult();
        };
        Task accept = server.WaitForConnectionAsync(timeout.Token);
        await pipe.ConnectAsync(timeout.Token);
        await accept;
        await pipe.WriteAsync(new IpcMessage((IpcMessageType)0x60, new byte[25]).ToBytes(), timeout.Token);
        await pipe.WriteAsync(new IpcMessage(IpcMessageType.WorkerReady).ToBytes(), timeout.Token);
        await sentinel.Task.WaitAsync(timeout.Token);
        Assert.Equal([IpcMessageType.WorkerReady], observed);
    }

    [Fact]
    public async Task AssociatedPipesDeliverOrderedTypedControlAndDiscardBufferedFramesOnLoss()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var owner = new ModLiveLaunchOwner();
        using var association = owner.CreateAssociation(Side.Client);
        using var workerAssociation = ModLiveLaunchAssociation.Parse(association.ToLaunchValue());
        string name = $"karpik-mod-live-{Guid.NewGuid():N}";
        using var server = new IpcServer(name);
        using var client = new IpcClient(name);
        server.ConfigureModLive(association);
        client.ConfigureModLive(workerAssociation);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));

        var host = Assert.IsType<ModLiveIpcPeer>(server.ModLivePeer);
        var worker = Assert.IsType<ModLiveIpcPeer>(client.ModLivePeer);
        Assert.Equal(association.Identity, host.Identity);
        Assert.Equal(host.Identity, worker.Identity);
        var operation = new ModLiveTransition.Operation(42, 3, 4);
        await host.SendAsync(new(ModLiveIpcKind.Prepare, operation, new byte[] { 1, 2 }), timeout.Token);
        await host.SendAsync(new(ModLiveIpcKind.Commit, operation), timeout.Token);
        Assert.True(await worker.WaitToReadAsync(timeout.Token));
        Assert.True(worker.TryRead(out var prepare));
        Assert.Equal(ModLiveIpcKind.Prepare, prepare.Kind);
        Assert.Equal(operation, prepare.Operation);
        Assert.Equal(new byte[] { 1, 2 }, prepare.Payload.ToArray());
        Assert.True(await worker.WaitToReadAsync(timeout.Token));
        Assert.True(worker.TryRead(out var commit));
        Assert.Equal(ModLiveIpcKind.Commit, commit.Kind);
        await worker.SendAsync(new(ModLiveIpcKind.Ready, operation, new byte[32]), timeout.Token);
        Assert.True(await host.WaitToReadAsync(timeout.Token));
        await client.StopAsync();
        await host.Loss.WaitAsync(timeout.Token);
        Assert.False(host.TryRead(out _));
        Assert.False(await host.WaitToReadAsync(timeout.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SendAsync(new(ModLiveIpcKind.Abort, operation), timeout.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssociationRejectsOtherGenerationOrOtherOwner(bool otherOwner)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var owner = new ModLiveLaunchOwner();
        using var association = owner.CreateAssociation(Side.Server);
        using var wrong = (otherOwner ? new ModLiveLaunchOwner() : owner).CreateAssociation(Side.Server);
        if (otherOwner) Assert.NotEqual(association.Identity.Session, wrong.Identity.Session);
        else Assert.NotEqual(association.Identity.Generation, wrong.Identity.Generation);
        string name = $"karpik-mod-live-{Guid.NewGuid():N}";
        using var server = new IpcServer(name);
        using var client = new IpcClient(name);
        server.ConfigureModLive(association);
        client.ConfigureModLive(wrong);
        Task accept = server.WaitForConnectionAsync(timeout.Token);
        Task connect = client.ConnectAsync(timeout.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => accept);
        await Assert.ThrowsAnyAsync<IOException>(() => connect);
        Assert.Null(server.ModLivePeer);
        Assert.Null(client.ModLivePeer);
    }

    [Fact]
    public async Task LegacyCaptureDoesNotBlockAssociatedControlOrShutdown()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var owner = new ModLiveLaunchOwner();
        using var association = owner.CreateAssociation(Side.Client);
        using var workerAssociation = ModLiveLaunchAssociation.Parse(association.ToLaunchValue());
        string name = $"karpik-mod-live-{Guid.NewGuid():N}";
        using var server = new IpcServer(name);
        using var client = new IpcClient(name);
        var captureEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captureReleased = new TaskCompletionSource<HotReloadState?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnModLiveStateRequest = token =>
        {
            captureEntered.SetResult();
            return captureReleased.Task.WaitAsync(token);
        };
        server.ConfigureModLive(association);
        client.ConfigureModLive(workerAssociation);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        var host = Assert.IsType<ModLiveIpcPeer>(server.ModLivePeer);
        var worker = Assert.IsType<ModLiveIpcPeer>(client.ModLivePeer);

        Task<(bool Received, HotReloadState? State)> state = server.TryRequestStateAsync(TimeSpan.FromSeconds(5), timeout.Token);
        await captureEntered.Task.WaitAsync(timeout.Token);
        Assert.Null(await server.RequestEditorSnapshotAsync(TimeSpan.FromSeconds(5), timeout.Token));
        await host.SendAsync(new(ModLiveIpcKind.Resume, new(1, 0, 1)), timeout.Token);
        Assert.True(await worker.WaitToReadAsync(timeout.Token));
        Assert.True(worker.TryRead(out var resume));
        Assert.Equal(ModLiveIpcKind.Resume, resume.Kind);
        captureReleased.SetResult(new() { Timestamp = 42 });
        Assert.Equal(42, (await state).State?.Timestamp);

        await server.SendShutdownRequestAsync(TimeSpan.FromSeconds(5), timeout.Token);
        await worker.Loss.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task CancelledProofDoesNotPublishPeer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancelProof = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var association = new ModLiveLaunchOwner().CreateAssociation(Side.Client);
        string name = $"karpik-mod-live-{Guid.NewGuid():N}";
        using var server = new IpcServer(name);
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        server.ConfigureModLive(association);
        Task accept = server.WaitForConnectionAsync(cancelProof.Token);
        await pipe.ConnectAsync(timeout.Token);
        Assert.Equal(ModLiveIpcProtocol.Challenge, (await IpcFrame.ReadAsync(pipe, new byte[5], timeout.Token))?.Type);
        cancelProof.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accept);
        Assert.Null(server.ModLivePeer);
        Assert.False(server.IsConnected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.WaitForConnectionAsync(timeout.Token));
    }

    [Fact]
    public async Task FullInboxFailsClosedRatherThanDroppingAPhase()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var owner = new ModLiveLaunchOwner();
        using var association = owner.CreateAssociation(Side.Server);
        var peer = new ModLiveIpcPeer(association.Identity, isOwner: true, (_, _) => Task.CompletedTask, () => { });
        var ready = ModLiveIpcProtocol.Encode(new(ModLiveIpcKind.Ready, new(1, 0, 1), new byte[32]), fromOwner: false);
        for (int i = 0; i < ModLiveIpcPeer.InboxCapacity; i++) Assert.True(peer.TryReceive(ready));
        Assert.False(peer.TryReceive(ready));
        await peer.Loss.WaitAsync(timeout.Token);
        Assert.False(peer.TryRead(out _));
    }
}
