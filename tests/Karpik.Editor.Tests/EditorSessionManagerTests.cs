using Karpik.Editor;
using Karpik.Engine.Core;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorSessionManagerTests
{
    [Fact]
    public async Task AddClientAsync_WithoutRunningServer_IsRejectedBeforeCreatingBackend()
    {
        var factory = new FakeEditorBackendFactory();
        using var manager = new EditorSessionManager(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.AddClientAsync(TestContext.Current.CancellationToken));

        Assert.Empty(factory.Created);
    }

    [Fact]
    public async Task StartServerThenAddClients_CreatesStableNamedSessionsAndSelectsNewestClient()
    {
        var factory = new FakeEditorBackendFactory();
        using var manager = new EditorSessionManager(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await manager.StartServerAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);

        Assert.Collection(
            manager.Sessions,
            session =>
            {
                Assert.Equal("Сервер", session.Name);
                Assert.Equal(Side.Server, session.Side);
            },
            session =>
            {
                Assert.Equal("Клиент 1", session.Name);
                Assert.Equal(Side.Client, session.Side);
            },
            session =>
            {
                Assert.Equal("Клиент 2", session.Name);
                Assert.Equal(Side.Client, session.Side);
            });
        Assert.Same(manager.Sessions[2], manager.SelectedSession);
        Assert.All(manager.Sessions, session => Assert.Equal(EditorPreviewState.Running, session.State));
    }

    [Fact]
    public async Task StopServerAsync_StopsAllClientsBeforeServer()
    {
        var stopOrder = new List<Side>();
        var factory = new FakeEditorBackendFactory(stopOrder);
        using var manager = new EditorSessionManager(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await manager.StartServerAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);

        await manager.StopSessionAsync(manager.Sessions[0], cancellationToken);

        Assert.Equal([Side.Client, Side.Client, Side.Server], stopOrder);
        Assert.All(manager.Sessions, session => Assert.Equal(EditorPreviewState.Stopped, session.State));
    }

    [Fact]
    public async Task StopServerAsync_WhenClientStopFails_StillStopsOtherClientsAndServer()
    {
        var factory = new FakeEditorBackendFactory();
        using var manager = new EditorSessionManager(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await manager.StartServerAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        factory.Created[1].StopException = new InvalidOperationException("client stop failed");

        await Assert.ThrowsAsync<AggregateException>(
            () => manager.StopSessionAsync(manager.Sessions[0], cancellationToken));

        Assert.Equal(1, factory.Created[1].StopCount);
        Assert.Equal(1, factory.Created[2].StopCount);
        Assert.Equal(1, factory.Created[0].StopCount);
        Assert.Equal(EditorPreviewState.Stopped, manager.Sessions[2].State);
        Assert.Equal(EditorPreviewState.Stopped, manager.Sessions[0].State);
    }

    [Fact]
    public async Task ServerFault_StopsAllClientsAndDisablesNewClients()
    {
        var factory = new FakeEditorBackendFactory();
        using var manager = new EditorSessionManager(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await manager.StartServerAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);

        factory.Created[0].EmitState(EditorPreviewState.Faulted);
        await Task.WhenAll(factory.Created[1].StopObserved, factory.Created[2].StopObserved)
            .WaitAsync(cancellationToken);

        Assert.False(manager.CanAddClient);
        Assert.Equal(EditorPreviewState.Stopped, manager.Sessions[1].State);
        Assert.Equal(EditorPreviewState.Stopped, manager.Sessions[2].State);
    }

    [Fact]
    public async Task RestartClientAsync_ReusesStableSessionWhileServerIsRunning()
    {
        var factory = new FakeEditorBackendFactory();
        using var manager = new EditorSessionManager(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await manager.StartServerAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        EditorSession client = manager.Sessions[1];
        await manager.StopSessionAsync(client, cancellationToken);

        await manager.RestartSessionAsync(client, cancellationToken);

        Assert.Equal(2, factory.Created[1].StartCount);
        Assert.Equal("Клиент 1", client.Name);
        Assert.Equal(EditorPreviewState.Running, client.State);
        Assert.Same(client, manager.SelectedSession);
    }

    [Fact]
    public async Task RestartServerAsync_StopsClientsWithoutRestartingThem()
    {
        var factory = new FakeEditorBackendFactory();
        using var manager = new EditorSessionManager(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await manager.StartServerAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        EditorSession server = manager.Sessions[0];

        await manager.RestartSessionAsync(server, cancellationToken);

        Assert.Equal(2, factory.Created[0].StartCount);
        Assert.Equal(EditorPreviewState.Running, server.State);
        Assert.Equal(1, factory.Created[1].StartCount);
        Assert.Equal(1, factory.Created[2].StartCount);
        Assert.Equal(EditorPreviewState.Stopped, manager.Sessions[1].State);
        Assert.Equal(EditorPreviewState.Stopped, manager.Sessions[2].State);
        Assert.Same(server, manager.SelectedSession);
    }

    [Fact]
    public async Task StopAllAsync_StopsClientsBeforeServer()
    {
        var stopOrder = new List<Side>();
        var factory = new FakeEditorBackendFactory(stopOrder);
        using var manager = new EditorSessionManager(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await manager.StartServerAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);

        await manager.StopAllAsync(cancellationToken);

        Assert.Equal([Side.Client, Side.Client, Side.Server], stopOrder);
        Assert.False(manager.CanAddClient);
    }

    [Fact]
    public async Task AddClientDuringServerStop_WaitsForLifecycleThenIsRejected()
    {
        var clientStopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeEditorBackendFactory(clientStopGate: clientStopGate);
        using var manager = new EditorSessionManager(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await manager.StartServerAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);

        Task stopTask = manager.StopSessionAsync(manager.Sessions[0], cancellationToken);
        await factory.Created[1].StopStarted.WaitAsync(cancellationToken);
        Task addTask = manager.AddClientAsync(cancellationToken);

        Assert.False(addTask.IsCompleted);
        clientStopGate.SetResult();
        await stopTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => addTask);
    }

    [Fact]
    public async Task SelectSessionAsync_ReturnsSnapshotOnlyFromSelectedBackend()
    {
        var factory = new FakeEditorBackendFactory();
        using var manager = new EditorSessionManager(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await manager.StartServerAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        factory.Created[0].Snapshot = CreateSnapshot(100);
        factory.Created[1].Snapshot = CreateSnapshot(200);

        manager.SelectSession(manager.Sessions[0]);
        EditorRuntimeSnapshot? serverSnapshot = await manager.RequestSelectedSnapshotAsync(
            TimeSpan.FromSeconds(1),
            cancellationToken);
        manager.SelectSession(manager.Sessions[1]);
        EditorRuntimeSnapshot? clientSnapshot = await manager.RequestSelectedSnapshotAsync(
            TimeSpan.FromSeconds(1),
            cancellationToken);

        Assert.Equal(100, Assert.Single(serverSnapshot!.Entities).EntityId);
        Assert.Equal(200, Assert.Single(clientSnapshot!.Entities).EntityId);
    }

    [Fact]
    public async Task RequestSelectedSnapshotAsync_DiscardsResponseAfterSelectionChanges()
    {
        var factory = new FakeEditorBackendFactory();
        using var manager = new EditorSessionManager(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await manager.StartServerAsync(cancellationToken);
        await manager.AddClientAsync(cancellationToken);
        var delayedSnapshot = new TaskCompletionSource<EditorRuntimeSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Created[0].SnapshotSource = delayedSnapshot;

        manager.SelectSession(manager.Sessions[0]);
        Task<EditorRuntimeSnapshot?> request = manager.RequestSelectedSnapshotAsync(
            TimeSpan.FromSeconds(1),
            cancellationToken);
        manager.SelectSession(manager.Sessions[1]);
        delayedSnapshot.SetResult(CreateSnapshot(100));

        Assert.Null(await request);
    }

    [Fact]
    public async Task SessionEvents_IdentifySourceSessionForDesktopUi()
    {
        var factory = new FakeEditorBackendFactory();
        using var manager = new EditorSessionManager(factory);
        var added = new List<EditorSession>();
        var selected = new List<EditorSession?>();
        var stateChanges = new List<(EditorSession Session, EditorPreviewState State)>();
        var output = new List<(EditorSession Session, string Line)>();
        manager.SessionAdded += added.Add;
        manager.SelectionChanged += selected.Add;
        manager.SessionStateChanged += (session, state) => stateChanges.Add((session, state));
        manager.OutputReceived += (session, line) => output.Add((session, line));

        await manager.StartServerAsync(TestContext.Current.CancellationToken);
        factory.Created[0].EmitOutput("ready");

        EditorSession server = Assert.Single(added);
        Assert.Same(server, Assert.Single(selected));
        Assert.Contains((server, EditorPreviewState.Running), stateChanges);
        Assert.Contains((server, "ready"), output);
    }

    private static EditorRuntimeSnapshot CreateSnapshot(int entityId) => new()
    {
        TotalEntityCount = 1,
        Entities = [new EditorEntitySnapshot { EntityId = entityId }]
    };

    private sealed class FakeEditorBackendFactory : IEditorBackendFactory
    {
        private readonly List<Side>? _stopOrder;
        private readonly TaskCompletionSource? _clientStopGate;

        public FakeEditorBackendFactory(
            List<Side>? stopOrder = null,
            TaskCompletionSource? clientStopGate = null)
        {
            _stopOrder = stopOrder;
            _clientStopGate = clientStopGate;
        }

        public List<FakeEditorBackend> Created { get; } = [];

        public IEditorBackend Create(Side side)
        {
            var backend = new FakeEditorBackend(
                side,
                _stopOrder,
                side == Side.Client ? _clientStopGate : null);
            Created.Add(backend);
            return backend;
        }
    }

    private sealed class FakeEditorBackend : IEditorBackend
    {
        private readonly List<Side>? _stopOrder;
        private readonly TaskCompletionSource? _stopGate;

        public FakeEditorBackend(
            Side side,
            List<Side>? stopOrder,
            TaskCompletionSource? stopGate)
        {
            Side = side;
            _stopOrder = stopOrder;
            _stopGate = stopGate;
        }

        public Side Side { get; }
        public EditorPreviewState State { get; private set; } = EditorPreviewState.Stopped;
        public int? ProcessId => State == EditorPreviewState.Running ? 1000 : null;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public EditorRuntimeSnapshot? Snapshot { get; set; }
        public TaskCompletionSource<EditorRuntimeSnapshot?>? SnapshotSource { get; set; }
        public Exception? StopException { get; set; }
        public Task StopObserved => _stopObserved.Task;
        public Task StopStarted => _stopStarted.Task;

        private readonly TaskCompletionSource _stopObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<EditorPreviewState>? StateChanged;
        public event Action<string>? OutputReceived;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            SetState(EditorPreviewState.Running);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            _stopStarted.TrySetResult();
            if (_stopGate is not null)
            {
                await _stopGate.Task.WaitAsync(cancellationToken);
            }

            StopCount++;
            _stopOrder?.Add(Side);
            if (StopException is not null)
            {
                throw StopException;
            }

            SetState(EditorPreviewState.Stopped);
            _stopObserved.TrySetResult();
        }

        public Task<EditorRuntimeSnapshot?> RequestSnapshotAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            SnapshotSource?.Task ?? Task.FromResult(Snapshot);

        public void EmitState(EditorPreviewState state) => SetState(state);

        public void EmitOutput(string line) => OutputReceived?.Invoke(line);

        private void SetState(EditorPreviewState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        public void Dispose()
        {
        }
    }
}
