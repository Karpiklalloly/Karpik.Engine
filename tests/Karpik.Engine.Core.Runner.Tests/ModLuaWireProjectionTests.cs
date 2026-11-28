using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using MoonSharp.Interpreter;
using Xunit;

[Collection(nameof(WorkingDirectoryCollection))]
public sealed class ModLuaWireProjectionTests
{
    private const string Pool = "provider:State";
    private const string Source = """
        return { components = {
          State = { version = 1, fields = { Value = 'i32', Large = 'i64', Private = 'i32' }, wire = {'Value', 'Large'} }
        } }
        """;

    [Fact]
    public void Shared_declaration_accepts_an_explicit_wire_list_without_changing_ECS_storage_schema()
    {
        using var runtime = Load(Source);
        var component = Assert.Single(runtime.Components);
        Assert.Equal(Pool, component.Id);
        Assert.Contains(component.Fields, field => field.Name == "Private" && field.Kind == SchemaScalarKind.I32);
    }

    [Theory]
    [InlineData("{ 'Value', 'Value' }", "duplicate")]
    [InlineData("{ 'Missing' }", "unknown field")]
    [InlineData("{ [2] = 'Value' }", "dense")]
    [InlineData("{ 9 }", "field name")]
    [InlineData("{ 'Data' }", "unsupported")]
    public void Malformed_projection_is_rejected_at_declaration(string projection, string diagnostic)
    {
        string source = $$"""
            return { components = { State = {
                version = 1, fields = { Value = 'i32', Data = 'nint' }, wire = {{projection}}
            } } }
            """;
        var error = Assert.Throws<InvalidDataException>(() => Load(source));
        Assert.Contains(Pool, error.Message);
        Assert.Contains(diagnostic, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Prepared_Lua_projection_has_a_distinct_claim_and_abort_keeps_the_accepted_projection()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Write("provider", Source);
        f.Write("other", "return {}", disabled: true);
        await f.LoadCompositionAsync();
        f.Start();
        var old = Assert.IsAssignableFrom<IRevisionReplicaProjection>(f.Preparation.AcceptedWireProjection);
        var hash = old.SchemaHash;
        int probeEntity = f.World.NewEntity();
        f.World.GetPool<NetworkReplica>().Add(probeEntity) = new() { Stream = 4, Id = 8, Incarnation = 1 };
        var previousBaseline = new RevisionReplicaBaseline(f.World, 4, new EmptyCompiledProjection(), old);
        Assert.True(previousBaseline.TryCapture([new(probeEntity, 8, 1)], 1));
        Assert.NotEqual(0, hash);

        using (var add = await f.PrepareAsync("provider", "other"))
        {
            add.PrepareActivation(default);
            var pending = Assert.IsAssignableFrom<IRevisionReplicaProjection>(f.Preparation.PendingWireProjection);
            Assert.NotSame(old, pending);
            Assert.Equal(hash, pending.SchemaHash);
            var futureBaseline = new RevisionReplicaBaseline(f.World, 4, new EmptyCompiledProjection(), pending);
            Assert.Throws<InvalidOperationException>(() => futureBaseline.TryCapture([new(probeEntity, 8, 1)], 2));
            Assert.Equal(13, f.Preparation.PendingWireRowBytes);
            Assert.Same(old, f.Preparation.AcceptedWireProjection);
        }
        Assert.Same(old, f.Preparation.AcceptedWireProjection);
        Assert.True(previousBaseline.TryCapture([new(probeEntity, 8, 1)], 1));

        using (var survivor = f.Bindings.CreateRevision())
        {
            survivor.Prepare(new("host", [], [], [new(Pool)]), "independent-host");
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => f.PrepareAsync());
            Assert.Contains("independent-host", error.Message);
        }
        using var remove = await f.PrepareAsync();
        remove.PrepareActivation(default);
        remove.Activate();
        remove.RetirePrevious(); // the retiring wire domain must not block its own empty pool
        Assert.Null(f.Preparation.AcceptedWireProjection);
        Assert.Throws<ObjectDisposedException>(() => previousBaseline.TryCapture([new(probeEntity, 8, 1)], 1));
    }

    [Fact]
    public async Task Real_lua_projection_round_trips_declared_values_preserving_private_fields()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Write("provider", Source);
        await f.LoadCompositionAsync();
        f.Start();
        var projection = Assert.IsAssignableFrom<IRevisionReplicaProjection>(f.Preparation.AcceptedWireProjection);
        int source = f.World.NewEntity(), replica = f.World.NewEntity();
        f.World.GetPool<NetworkReplica>().Add(replica) = new() { Stream = 5, Id = 73, Incarnation = 1 };
        using var writerDomain = f.Bindings.CreateRevision();
        var binding = writerDomain.Prepare(new("seed", [], [], [new(Pool, false)]), "test").Components[Pool];
        var value = binding.Fields.Span.ToArray().Single(field => field.Name == "Value").I32;
        var large = binding.Fields.Span.ToArray().Single(field => field.Name == "Large").I64;
        var local = binding.Fields.Span.ToArray().Single(field => field.Name == "Private").I32;
        writerDomain.ActivateRevision();
        writerDomain.CallbackScope.Enter("test");
        try
        {
            binding.Add(source);
            binding.Add(replica);
            value.Write(source, int.MinValue);
            large.Write(source, long.MaxValue);
            local.Write(source, 7);
            local.Write(replica, 99);
        }
        finally { writerDomain.CallbackScope.Exit(); }

        var baseline = new RevisionReplicaBaseline(f.World, 5, new EmptyCompiledProjection(), projection);
        Assert.True(baseline.TryCapture([new(source, 73, 1)], 2));
        Assert.False(baseline.TryApply(baseline.Encoded[..^1], 2));
        Assert.True(baseline.TryApply(baseline.Encoded, 2));
        Assert.True(baseline.TryFind(73, 1, out int kept));
        Assert.Equal(replica, kept);
        writerDomain.CallbackScope.Enter("test");
        try
        {
            Assert.Equal(int.MinValue, value.Read(kept));
            Assert.Equal(long.MaxValue, large.Read(kept));
            Assert.Equal(99, local.Read(kept));
        }
        finally { writerDomain.CallbackScope.Exit(); }
    }

    [Fact]
    public async Task Release_survivor_cannot_change_wire_projection_through_optional_dependency()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Write("modifier", "return {}", disabled: true);
        f.Write("provider", Source.Replace("{'Value', 'Large'}", "(has_dependency('modifier') and {'Value'} or {'Value', 'Large'})"),
            manifest: """{"id":"provider","dependencies":{"shared":[{"id":"modifier","optional":true}]}}""");
        await f.LoadCompositionAsync();
        f.Start();
        var accepted = f.Preparation.AcceptedWireProjection;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => f.PrepareAsync("modifier", "provider"));
        Assert.Contains("wire", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(accepted, f.Preparation.AcceptedWireProjection);
    }

    private sealed class EmptyCompiledProjection : IRevisionReplicaProjection
    {
        public long SchemaHash => 1;
        public void Write(EcsWorld world, int entity, IWriter writer) { }
        public void Validate(IReader reader) { }
        public void Apply(EcsWorld world, int entity, IReader reader) { }
    }

    private static LuaScriptRuntime Load(string source)
    {
        var script = new Script(CoreModules.Preset_HardSandbox);
        return new LuaScriptRuntime(script, [script.DoString(source).Table], "provider", ["Shared/main.lua"], sharedEntryCount: 1);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("karpik-mod-wire-").FullName;
        private readonly JobSystem _jobs = new(workerCount: 2, prefix: nameof(ModLuaWireProjectionTests));
        private readonly ComponentCallbackScope _scope = new();
        private readonly Application _application = new(Side.Server);
        private readonly ModsRegistry _registry;
        private readonly ModsLifecycleManager _lifecycle;
        internal readonly EcsDefaultWorld World = new();
        internal readonly ComponentQueryPreparer Bindings;
        internal readonly LuaSimulationPreparation Preparation;

        internal Fixture()
        {
            string previous = Environment.CurrentDirectory;
            PhysicalFileSystem fs;
            try { Environment.CurrentDirectory = _root; fs = new PhysicalFileSystem(); }
            finally { Environment.CurrentDirectory = previous; }
            Job.Initialize(_jobs);
            _registry = new ModsRegistry(fileSystem: fs);
            _lifecycle = new ModsLifecycleManager(new LuaScriptLoader(fs, NullLogger<IScriptLoader>.Instance));
            var components = new LuaComponentRegistry(new ClrComponentBindings([]));
            var schemas = new ComponentSchemaRegistry();
            Bindings = new ComponentQueryPreparer(World, components.Bindings, schemas, _scope);
            Preparation = new LuaSimulationPreparation(_registry, _lifecycle, _application,
                new SystemRegistry(), new Time(), components, schemas, Bindings, _scope,
                new ScriptSystemOptions(), NullLogger<IScriptLoader>.Instance);
        }

        internal void Write(string id, string source, bool disabled = false, string? manifest = null)
        {
            string mod = Path.Combine(_root, "Mods", id);
            Directory.CreateDirectory(Path.Combine(mod, "Shared"));
            File.WriteAllText(Path.Combine(mod, "mod_info.json"), manifest ?? $$"""{"id":"{{id}}"}""");
            File.WriteAllText(Path.Combine(mod, "Shared", "main.lua"), source);
            if (disabled) File.WriteAllText(Path.Combine(_root, "mods.json"), $$"""{"disabled":["{{id}}"]}""");
        }

        internal async Task LoadCompositionAsync()
        {
            var startup = new LuaModSetPreparation(_registry, _application);
            await startup.PrepareAsync(default);
            startup.CompleteStartupWithoutContent(default);
        }

        internal void Start() => Preparation.PrepareAsync(default).GetAwaiter().GetResult();

        internal async Task<ISimulationCodeReloadCandidate> PrepareAsync(params string[] ids)
        {
            var basis = Preparation.AcceptedComposition!;
            var target = ModsRegistry.ComposeSelection(_registry.DiscoverSelection(basis, ids), []);
            return await Preparation.PrepareLiveAsync(basis, target, default);
        }

        public void Dispose()
        {
            Preparation.Dispose();
            _lifecycle.Dispose();
            Bindings.Dispose();
            _scope.Dispose();
            World.Destroy();
            Job.ShutdownIfCurrent(_jobs);
            Directory.Delete(_root, recursive: true);
        }
    }
}
