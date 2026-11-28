using System.Buffers.Binary;
using System.Collections.Concurrent;
using Autofac;
using DCFApixels.DragonECS;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib;
using Microsoft.Extensions.Logging;

namespace Karpik.Content.Reload.HeadlessHost;

// Reference headless game composition. All world/network operations run on the platform owner or the reserved gameplay frame.
internal sealed partial class LiveProbe(Side side, string mode, int port) : IDisposable
{
    private const long InitialRevision = (long)ModLiveIpcProtocol.InitialRevision;
    private const long CompiledHash = 0x3A9E;
    private const string GameKey = "headless-game"; // Ordinary application admission, not the paired-session capability.
    private readonly LiteNetLibNetworkManager _net = new();
    private readonly ConcurrentQueue<string> _commands = new();
    private readonly Queue<(IPeer Peer, long Revision, int Value)> _input = new(64);
    private readonly Queue<(IPeer Peer, int Value)> _effects = new(64);
    private readonly Projection _compiled = new();
    private readonly IPeer[] _cohort = new IPeer[1];
    private IWriter _writer = null!;
    private EngineRunner _runner = null!;
    private EcsDefaultWorld _world = null!;
    private ModsRegistry _mods = null!;
    private LuaSimulationPreparation _lua = null!;
    private MainThreadScheduler _scheduler = null!;
    private IContentRegistry _content = null!;
    private AssetId _asset;
    private Stream? _oldRead;
    private int _connections, _entity = -1, _effect, _maximumAdditionalRows;
    private TaskCompletionSource? _holdPrepared;
    private IPeer? _first;
    private PairSecurity? _pair;
    private bool _stopped;
#if DEBUG
    private ISimulationSchemaReloadHost? _schemaHost;
    private DebugSchemaReload? _schema;
    private Task<SimulationCodeReloadStatus>? _adminRequest;
    private bool _admin;
    private string? _artifact;
    private IPeer? _artifactServer;
#endif

    internal static async Task<int> RunAsync(Side side, string mode, string[] args)
    {
        Console.SetOut(new LiveOutput(Console.Out));
        var port = int.Parse(Environment.GetEnvironmentVariable("KARPIK_HEADLESS_LIVE_PORT")!);
        using var probe = new LiveProbe(side, mode, port);
        using var stop = new CancellationTokenSource();
        var token = stop.Token;
        _ = Task.Run(async () =>
        {
            try { while (await Console.In.ReadLineAsync(token) is { } line) probe._commands.Enqueue(line); }
            catch (OperationCanceledException) { }
        });
        try
        {
            var launch = StaticEngineHost.ParseLaunchArguments(side, args);
            await new WorkerHost().RunAsync(launch, (_, _, _, bootstrap) => probe.Configure((EngineRunner)bootstrap.Runner));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { stop.Cancel(); }
    }

    private WorkerRuntimeConfiguration Configure(EngineRunner runner)
    {
        _runner = runner;
        runner.UpdateSchedulerMode = Karpik.Engine.Shared.ECS.Scheduling.EcsUpdateSchedulerMode.Deterministic;
        runner.RegisterTypes([.. typeof(ModsRegistry).Assembly.GetTypes(), .. typeof(IModsRegistry).Assembly.GetTypes()]);
        runner.RegisterModule(new NetworkModuleInstaller());
        runner.RegisterModule(new LiveServices(this));
        runner.RegisterModule(new LiveSimulation());
        WorkerModLiveSupport? paired = null;
        if (mode == "paired")
        {
            _pair = new PairSecurity(_net);
            paired = new(Discover, ValidatePrepared, NetworkPhase, _pair.BindIdentity, _pair.WireLoss);
        }
#if DEBUG
        else if (mode == "dedicated") _schemaHost = runner.EnableDebugSchemaReload(InitialRevision);
#endif
        else throw new ArgumentException("Unsupported live fixture mode.");
        return new(AppContext.BaseDirectory, null, paired, Pump, StopControl);
    }

    private void Bind(EcsDefaultWorld world, ModsRegistry mods, LuaSimulationPreparation lua,
        MainThreadScheduler scheduler, IContentRegistry content, FileContentStore store)
    {
        _world = world; _mods = mods; _lua = lua; _scheduler = scheduler; _content = content;
        var asset = store.CapturedManifest!.Entries.Single();
        _asset = asset.AssetId;
        _oldRead = store.OpenRead(asset.ArtifactLocator);
        _net.ConfigureProtocolSchema(CompiledHash);
        _net.ConfigureRevisionTransitions(InitialRevision);
        _writer = _net.CreateWriter();
        _net.ConnectionRequestEvent += request => request.AcceptIfKey(GameKey);
        _net.PeerConnectedEvent += peer =>
        {
            _first ??= peer;
            _connections++;
            _pair?.Offer(peer);
            Console.WriteLine($"live:connected:{_connections}");
        };
        _net.NetworkReceiveEvent += NetworkGameplay;
        _net.RevisionControlReceived += OnPairedControl;
        _net.Start(side == Side.Server ? port : 0);
#if DEBUG
        if (_schemaHost is { } schemaHost)
            _schema = new(_net, schemaHost, (peer, digest) => digest == _artifact && ReferenceEquals(peer, _artifactServer), ApplicationDrained, Baseline,
                ClearDeferred, _compiled, BaselineBudget, TimeSpan.FromSeconds(8), () => _admin);
#endif
        if (side == Side.Server) Console.WriteLine("live:listening");
        else _net.Connect("127.0.0.1", port, GameKey);
    }

    private async ValueTask<IReadOnlyCollection<string>> Discover(CancellationToken token)
    {
        if (_pair is null || !await _pair.Authenticated.Task.WaitAsync(token).ConfigureAwait(false) ||
            _first is not { } peer || !_pair.Authorizes(peer))
            throw new InvalidOperationException("No owned revision peer.");
        token.ThrowIfCancellationRequested();
        var target = side == Side.Server ? AssetTarget.Server : AssetTarget.Client;
        return _mods.DiscoverInstalled(target, token).InstalledOwnerIds;
    }

    private async ValueTask<ReadOnlyMemory<byte>> ValidatePrepared(EngineRunner.LiveModReload reload, CancellationToken token)
    {
        var hold = await _scheduler.InvokeAsync(() =>
        {
            if (_holdPrepared is not null) Console.WriteLine("live:prepared-held");
            return _holdPrepared?.Task;
        });
        if (hold is not null) await hold.WaitAsync(token).ConfigureAwait(false);
        return await _scheduler.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (_first is not { } peer || _pair?.Authorizes(peer) != true) return default;
            if (reload.MaximumModReplicaRowBytes is not >= 0) return default;
            if (side == Side.Server)
            {
                if (!BaselineBudget(peer).TryReserve(_compiled, reload.MaximumModReplicaRowBytes, out _pairedMaximumRows)) return default;
            }
            else if (Baseline(null, (long)reload.Operation.Basis)?.CanReconcileExistingStream() != true) return default;
            return PreparedFingerprint(reload);
        });
    }

    private static ReadOnlyMemory<byte> PreparedFingerprint(EngineRunner.LiveModReload reload)
    {
        if (reload.SharedSchemaProof.Length != 32) return default;
        Span<byte> input = stackalloc byte[sizeof(long) + 32];
        BinaryPrimitives.WriteInt64LittleEndian(input, CompiledHash);
        reload.SharedSchemaProof.Span.CopyTo(input[sizeof(long)..]);
        return System.Security.Cryptography.SHA256.HashData(input);
    }

    private void Pump()
    {
        _net.PollEvents();
        while (_effects.TryDequeue(out var effect)) SendGameplay(effect.Peer, effect.Value);
        if (_entity < 0) FindActor();
        AdvancePaired();
#if DEBUG
        _schema?.Advance();
        if (_adminRequest is { IsCompletedSuccessfully: true } finished)
        {
            Console.WriteLine($"live:reload:{finished.Result}");
            _adminRequest = null;
        }
#endif
        while (_commands.TryDequeue(out string? request))
        {
            if (request == "sample") Sample();
            else if (side == Side.Server && request == "hold-prepared")
            {
                _holdPrepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Console.WriteLine("live:will-hold-prepared");
            }
            else if (side == Side.Client && request == "disconnect-wire" && _first is LiteNetLibPeer wire) wire.Disconnect();
            else if (side == Side.Client && request == "seed-client-replicas")
            {
                var replicas = _world.GetPool<NetworkReplica>();
                for (int id = 2; id <= RevisionReplicaBaseline.MaxReplicas + 1; id++)
                    replicas.Add(_world.NewEntity()) = new NetworkReplica { Stream = 1, Id = id, Incarnation = 1 };
                Console.WriteLine($"live:replica-count:{replicas.Count}");
            }
            else if (side == Side.Client && request == "drop-last-client-replica")
            {
                var replicas = _world.GetPool<NetworkReplica>();
                foreach (int entity in replicas.ToSpan())
                {
                    if (replicas.Get(entity).Id != RevisionReplicaBaseline.MaxReplicas + 1) continue;
                    _world.DelEntity(entity);
                    break;
                }
                _world.ReleaseDelEntityBufferAll();
                Console.WriteLine($"live:replica-count:{replicas.Count}");
            }
            else if (side == Side.Server && request.StartsWith("baseline-growth:", StringComparison.Ordinal))
            {
                _maximumAdditionalRows = int.Parse(request.AsSpan(16));
                Console.WriteLine($"live:baseline-growth:{_maximumAdditionalRows}");
            }
            else if (request.StartsWith("input:", StringComparison.Ordinal)) SendGameplay(_first!, int.Parse(request.AsSpan(6)));
#if DEBUG
            else if (request == "grant-admin" && side == Side.Server) _admin = true;
            else if (request.StartsWith("grant-artifact:", StringComparison.Ordinal) && request.Length == 79)
            {
                _artifact = request[15..];
                _artifactServer = side == Side.Client ? _first : null;
                Console.WriteLine("live:grant:artifact");
            }
            else if (request == "reload" && _schema is not null && _adminRequest is null) _adminRequest = _schema.RequestAsync();
#endif
        }
    }

    private void FindActor()
    {
        var anchors = _world.GetPool<DynamicComponentAnchor>();
        if (anchors.Count == 0) return;
        for (int i = 0; i < _world.Capacity; i++)
        {
            if (!_world.IsUsed(i) || !anchors.Has(i)) continue;
            _entity = i;
            if (side == Side.Client)
                _world.GetPool<NetworkReplica>().Add(i) = new NetworkReplica { Stream = 1, Id = 1, Incarnation = 1 };
            return;
        }
    }

    private void NetworkGameplay(IPeer peer, IReader reader, byte channel, DeliveryMethod delivery)
    {
        if (reader.AvailableBytes != sizeof(int) + 1 || reader.GetByte() != (byte)PacketType.Command) return;
        _input.Enqueue((peer, reader.GameplayRevision, reader.GetInt()));
    }

    private void DispatchGameplay()
    {
        // The gameplay frame consumes deferred events, including the revision recheck. The platform owner sends effects afterward.
        while (_input.TryDequeue(out var accepted))
        {
            if (accepted.Revision != _net.GameplayRevision) continue;
            _effect = accepted.Value;
            if (side == Side.Server) _effects.Enqueue((accepted.Peer, accepted.Value));
            else Console.WriteLine($"live:effect:{accepted.Value}");
        }
    }

    private void SendGameplay(IPeer peer, int value)
    {
        _writer.Reset(); _writer.Put((byte)PacketType.Command); _writer.Put(value);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }

    private bool ApplicationDrained()
    {
        if (_input.Count != 0 || _effects.Count != 0) return false;
        _world.ReleaseDelEntityBufferAll();
        return true;
    }
    private void ClearDeferred() { _input.Clear(); _effects.Clear(); }

    // This game exposes only its one registered actor. Lua on_start cannot add new visibility to that registry.
    // The fixture can deliberately declare a larger hard reserve to test the budget boundary.
    private RevisionBaselineBudget BaselineBudget(IPeer peer) =>
        _entity >= 0 ? new(_world, new[] { new RevisionReplicaIdentity(_entity, 1, 1) }, _maximumAdditionalRows) : default;
    private RevisionReplicaBaseline? Baseline(IPeer? peer, long revision)
    {
        var baseline = new RevisionReplicaBaseline(_world, 1, _compiled, _lua.AcceptedWireProjection);
        if (side == Side.Client) return baseline;
        return _entity >= 0 && baseline.TryCapture([new(_entity, 1, 1)], revision) ? baseline : null;
    }

    private void Sample()
    {
        if (_entity < 0 || _first is null) throw new InvalidOperationException("The live actor or peer has not appeared.");
        long rev = _net.GameplayRevision;
        var baseline = new RevisionReplicaBaseline(_world, 1, _compiled, _lua.AcceptedWireProjection);
        if (!baseline.TryCapture([new(_entity, 1, 1)], rev)) throw new InvalidOperationException("No fixture row.");
        // Header (4 longs + count), identity (int + long), mod presence, then the sorted mod fields.
        ReadOnlySpan<byte> wire = baseline.Encoded.Span[(4 * sizeof(long) + sizeof(int) + sizeof(int) + sizeof(long))..];
        if (wire[0] != 1) throw new InvalidOperationException("The Lua actor did not publish its dynamic row.");
        bool migrated = wire.Length == 17;
        int name = BinaryPrimitives.ReadInt32LittleEndian(wire[(migrated ? 5 : 1)..]);
        int added = migrated ? BinaryPrimitives.ReadInt32LittleEndian(wire[1..]) : 0;
        long value = migrated ? BinaryPrimitives.ReadInt64LittleEndian(wire[9..]) : BinaryPrimitives.ReadInt32LittleEndian(wire[5..]);
        if (!((IContentUpdateRegistry)_content).TryResolve<byte[]>(_asset, out var asset)) throw new InvalidOperationException("No asset.");
        _content.LoadAsync(asset).GetAwaiter().GetResult();
        if (!_content.TryGet(asset, out var payload)) throw new InvalidOperationException("No asset payload.");
        if (_settledOperation != 0)
        {
            var metrics = _runner.LastCodeReloadMetrics;
            Console.WriteLine(FormattableString.Invariant($"live:metrics:{_settledOperation}:pauseMs={metrics.PauseSeconds * 1000:F3}:prepareMs={metrics.PreparationSeconds * 1000:F3}:commitMs={metrics.CommitSeconds * 1000:F3}:retireMs={metrics.RetirementSeconds * 1000:F3}:processAllocatedBytes={metrics.ProcessAllocatedBytes}"));
            _settledOperation = 0;
        }
        _oldRead!.Position = 9;
        Console.WriteLine($"live:sample:{rev},{(long)_world.GetEntityLong(_entity)},{_first.Id},{_connections},{value},{name},{added},{payload.Payload[9] - '0'},{_effect},{_oldRead.ReadByte() - '0'}");
    }

    private void StopControl()
    {
        if (_stopped) return;
        _stopped = true;
#if DEBUG
        _schema?.Dispose();
#endif
        _networkWork?.Done.TrySetException(new IOException("The live worker is stopping."));
        _net.Stop();
    }
    public void Dispose() { StopControl(); _pair?.Dispose(); _oldRead?.Dispose(); _net.Dispose(); }

    [Module(ModuleScope.Engine)]
    private sealed class LiveServices(LiveProbe probe) : IModuleInstaller
    {
        public string Name => nameof(LiveServices);
        public void OnRegisterServices(ContainerBuilder services)
        {
            services.RegisterInstance(probe).ExternallyOwned();
            services.Register(_ => new PhysicalFileSystem()).As<IFileSystem>().SingleInstance();
            services.RegisterType<LuaProbeLog>().As<ILogger<IScriptLoader>>().SingleInstance();
        }
    }
    [Module(ModuleScope.Simulation)]
    private sealed class LiveSimulation : IModuleInstaller, IModule
    {
        public string Name => nameof(LiveSimulation);
        public IModule CreateModule() => this;
        public void Add(ISystemRegistry systems) => systems.Add<Game>();
        public void OnRegisterServices(ContainerBuilder services)
        {
            services.Register(_ => new EcsDefaultWorld()).SingleInstance().OnRelease(world => world.Destroy());
        }
    }
    private sealed class Game(LiveProbe probe, EcsDefaultWorld world, IModsRegistry mods, ISimulationPreparation[] preparations,
        MainThreadScheduler scheduler, IContentRegistry content, FileContentStore store) : ISystemBegin, ISystemAsyncInit
    {
        public void Begin() => probe.DispatchGameplay();
        public ValueTask InitAsync(CancellationToken token)
        {
            probe.Bind(world, (ModsRegistry)mods, preparations.OfType<LuaSimulationPreparation>().Single(), scheduler, content, store);
            return ValueTask.CompletedTask;
        }
    }
    private sealed class LuaProbeLog : ILogger<IScriptLoader>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? error, Func<TState, Exception?, string> format)
        {
            string message = format(state, error);
            if (message == "[Mod: patch] patch-start") Console.WriteLine("live:lua:patch-start");
            else if (message == "[Mod: patch] patch-destroy") Console.WriteLine("live:lua:patch-destroy");
            else if (level >= LogLevel.Error) Console.Error.WriteLine(message);
        }
    }
    private sealed class Projection : IRevisionReplicaProjection
    {
        public long SchemaHash => CompiledHash;
        public int? MaximumRowBytes => 0;
        public void Write(EcsWorld world, int entity, IWriter writer) { }
        public void Validate(IReader reader) { }
        public void Apply(EcsWorld world, int entity, IReader reader) { }
    }
    private sealed class LiveOutput(TextWriter output) : TextWriter
    {
        public override System.Text.Encoding Encoding => output.Encoding;
        public override void WriteLine(string? value)
        {
            if (value?.StartsWith("live:", StringComparison.Ordinal) == true) output.WriteLine(value);
            else if (value?.Contains("Error", StringComparison.OrdinalIgnoreCase) == true) Console.Error.WriteLine(value);
        }
    }
}
