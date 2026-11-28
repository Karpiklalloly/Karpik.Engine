using System.Reflection;
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
using Xunit;

[Collection(nameof(WorkingDirectoryCollection))]
public sealed class ModLiveMigrationTests
{
    private const string Pool = "probe:State";
    private const string Composite = "Vector = { kind = 'fixed', element = 'i32', length = 2 }";
    private const string Stable = "components = { Stable = { version = 1, fields = { Count = 'i32' } },";
    private const string Initial = """
        return {
          components = { State = { version = 1, fields = { Value = 'i32', Keep = 'i32', Name = 'i32' }, wire = {'Value'} } },
          systems = {{ id = 'probe', phase = 'update', callback = function() end,
            queries = { scan = { opt = { ['probe:State'] = { readonly = false } } } } }}
        }
        """;
    private const string Changed = """
        return {
          components = { State = { version = 2, fields = { Wide = 'i64', Keep = 'i32', Renamed = 'i32', Added = 'i32' }, wire = {'Wide', 'Added'},
            migrate = { from = 1, fields = { Wide = { from = 'Value', convert = 'i32_to_i64' }, Renamed = { from = 'Name' }, Added = { default = -77 } } } } },
          on_start = function() error('a migrated replacement must not start') end,
          systems = {{ id = 'new', phase = 'update', callback = function(dt,ctx)
                assert(ctx.queries.scan.components['probe:State'] ~= nil)
            end, queries = { scan = { opt = { ['probe:State'] = { readonly = false } } } } }}
        }
        """;

#if DEBUG
    [Fact]
    public async Task ArtifactPolicyRejectsBeforeTopLevelLuaExecutes()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        await f.LoadAsync();
        f.Start();
        var basis = f.Preparation.AcceptedComposition;
        f.Replace("error('Lua ran before consent')");
        var participant = Assert.IsAssignableFrom<ISimulationSchemaReloadParticipant>(f.Preparation);
        string? digest = null;
        var rejected = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await participant.PrepareSchemaReloadAsync(value => { digest = value; return false; }, default));
        Assert.Equal(64, digest?.Length);
        Assert.DoesNotContain("Lua ran", rejected.Message);
        Assert.Same(basis, f.Preparation.AcceptedComposition);
        Assert.Null(f.Preparation.PendingWireProjection);
    }

    [Fact]
    public async Task PreparedSharedProofChangesForUnprojectedSharedSchemaAndIsUnavailableBeforeReady()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        await f.LoadAsync();
        f.Start();
        var participant = Assert.IsAssignableFrom<ISimulationSchemaReloadParticipant>(f.Preparation);
        byte[] Proof(string text)
        {
            f.Replace(text);
            using var candidate = participant.PrepareSchemaReloadAsync(_ => true, default).GetAwaiter().GetResult();
            Assert.True(candidate.SharedSchemaProof.IsEmpty);
            candidate.PrepareActivation(default);
            return candidate.SharedSchemaProof.ToArray();
        }
        var first = Proof(Changed);
        Assert.Equal(32, first.Length);
        Assert.Equal(first, Proof(Changed));
        Assert.False(first.AsSpan().SequenceEqual(Proof(Changed.Replace("Keep = 'i32',", "Keep = 'i32', Extra = 'i32',")
            .Replace("Renamed = { from = 'Name' }", "Renamed = { from = 'Name' }, Extra = { default = 8 }"))));
    }

    [Fact]
    public async Task PreparedSharedSchemaProofUsesDeclarationsNotPrivateSideBytes()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        await f.LoadAsync();
        f.Start();
        f.Replace("local kind = require('proof_kind')\n" + Changed.Replace("Keep = 'i32',", "Keep = 'i32', Extra = kind,")
            .Replace("Renamed = { from = 'Name' }", "Renamed = { from = 'Name' }, Extra = { default = 8 }"));
        var participant = Assert.IsAssignableFrom<ISimulationSchemaReloadParticipant>(f.Preparation);
        (byte[] Proof, string? Artifact) Prepare(string helper)
        {
            f.WriteSideHelper(helper);
            string? artifact = null;
            using var candidate = participant.PrepareSchemaReloadAsync(digest => { artifact = digest; return true; }, default)
                .GetAwaiter().GetResult();
            candidate.PrepareActivation(default);
            return (candidate.SharedSchemaProof.ToArray(), artifact);
        }
        var first = Prepare("return 'i32'");
        var cosmetic = Prepare("-- only a side comment\nreturn 'i32'");
        var differentSchema = Prepare("return 'i16'");
        Assert.NotEqual(first.Artifact, cosmetic.Artifact);
        Assert.Equal(first.Proof, cosmetic.Proof);
        Assert.False(first.Proof.AsSpan().SequenceEqual(differentSchema.Proof));
    }

    [Fact]
    public async Task SideOnlyEntryAttestsSharedHelperButNotPrivateSideSource()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        const string entry = "assert(require('shared_helper') ~= nil); return {}";
        f.WriteScript("Shared", "shared_helper.lua", "return 1");
        await f.LoadAsync(entry, sideEntry: true);
        f.Start();
        var participant = (ISimulationSchemaReloadParticipant)f.Preparation;
        byte[] Proof(string helper, string source)
        {
            f.WriteScript("Shared", "shared_helper.lua", helper);
            f.WriteScript("Server", "main.lua", source);
            using var candidate = participant.PrepareSchemaReloadAsync(_ => true, default).GetAwaiter().GetResult();
            candidate.PrepareActivation(default);
            return candidate.SharedSchemaProof.ToArray();
        }
        var first = Proof("return 1", entry);
        Assert.Equal(first, Proof("return 1", "-- private side change\n" + entry));
        Assert.False(first.AsSpan().SequenceEqual(Proof("return 2", entry)));
    }

    [Fact]
    public async Task MigrationStagesExactValuesWireAndQueries_AndAbortKeepsOldWorld()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        await f.LoadAsync(Initial.Replace("Name = 'i32'", "Name = 'i32', " + Composite).Replace("components = {", Stable));
        f.Start();
        var oldContext = f.Context;
        int min = f.Seed(int.MinValue, vector: true), max = f.Seed(int.MaxValue, vector: true);
        var stable = f.Bindings.Prepare(new("stable", [], [], [new("probe:Stable", Readonly: false)]), "stable-reader").Components["probe:Stable"];
        f.Bindings.CallbackScope.Enter("stable-reader");
        try { stable.Add(min); stable.Get(min).WriteI32(0, 96); }
        finally { f.Bindings.CallbackScope.Exit(); }
        entlong minIdentity = f.World.GetEntityLong(min), maxIdentity = f.World.GetEntityLong(max);
        long worldVersion = f.World.Version;
        var basis = f.Preparation.AcceptedComposition;
        var oldWire = Assert.IsAssignableFrom<IRevisionReplicaProjection>(f.Preparation.AcceptedWireProjection);
        var oldBaseline = f.Baseline(oldWire);
        Assert.True(oldBaseline.TryCapture([new(min, 4, 1), new(max, 8, 1)], 1));
        byte[] oldBytes = oldBaseline.Encoded.ToArray();
        f.Replace(Changed.Replace("Renamed = 'i32'", "Renamed = 'i32', " + Composite).Replace("components = {", Stable));
        f.InstallUnselected();

        using (var candidate = await f.PrepareAsync())
        {
            candidate.PrepareActivation(default);
            Assert.NotEqual(oldWire.SchemaHash, f.Preparation.PendingWireProjection!.SchemaHash);
            Assert.Equal(13, f.Preparation.PendingWireRowBytes); // presence + i32 + i64
            Assert.Same(basis, f.Preparation.AcceptedComposition);
            Assert.Same(oldWire, f.Preparation.AcceptedWireProjection);
            Assert.Same(oldContext, f.Context);
            Assert.Equal(worldVersion, f.World.Version);
            Assert.Equal(int.MinValue, Fixture.Read32(oldContext, min, "Value"));
            Assert.True(oldBaseline.TryCapture([new(min, 4, 1), new(max, 8, 1)], 1));
            Assert.Equal(oldBytes, oldBaseline.Encoded.ToArray());
        }
        Assert.Null(f.Preparation.PendingWireProjection);
        Assert.Same(oldWire, f.Preparation.AcceptedWireProjection);
        Assert.Equal(int.MaxValue, Fixture.Read32(oldContext, max, "Value"));

        using var retry = await f.PrepareAsync();
        retry.PrepareActivation(default);
        var pendingWire = f.Preparation.PendingWireProjection;
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        retry.Activate();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Assert.Equal(0, allocated);
        retry.RetirePrevious();
        Assert.Same(pendingWire, f.Preparation.AcceptedWireProjection);
        Assert.Throws<ObjectDisposedException>(oldContext.Enter);
        Assert.Throws<ObjectDisposedException>(() => oldBaseline.TryCapture([new(min, 4, 1)], 1));
        Assert.Equal(minIdentity, f.World.GetEntityLong(min));
        Assert.Equal(maxIdentity, f.World.GetEntityLong(max));
        Assert.Equal(worldVersion, f.World.Version);
        Assert.Equal(2, f.World.GetPool<DynamicComponentAnchor>().Count);
        Assert.Single(f.Lifecycle.Containers); // Fresh capture cannot add newly installed scripts.
        Assert.Single(f.Preparation.AcceptedComposition!.PackagesById);
        Assert.Equal((long)int.MinValue, Fixture.Read64(f.Context, min, "Wide"));
        Assert.Equal((long)int.MaxValue, Fixture.Read64(f.Context, max, "Wide"));
        Assert.Equal(16, Fixture.Read32(f.Context, min, "Renamed"));
        Assert.Equal(3, Fixture.Read32(f.Context, max, "Keep"));
        Assert.Equal(-77, Fixture.Read32(f.Context, min, "Added"));
        Assert.Equal(-77, Fixture.Read32(f.Context, max, "Added"));
        Assert.Equal(25, Fixture.Read32(f.Context, min, "Vector[1]"));
        Assert.Equal(31, Fixture.Read32(f.Context, max, "Vector[2]"));
        f.Bindings.CallbackScope.Enter("stable-reader");
        try
        {
            Assert.Equal(96, stable.Get(min).ReadI32(0));
            stable.Del(min);
            Assert.True(f.World.GetPool<DynamicComponentAnchor>().Has(min));
        }
        finally { f.Bindings.CallbackScope.Exit(); }
        Assert.True(f.Baseline(pendingWire!).TryCapture([new(min, 4, 1), new(max, 8, 1)], 2));
        f.Preparation.ActiveSystems.Span[0].Update();
        var acceptedWire = f.Preparation.AcceptedWireProjection;
        using (var codeOnly = await f.PrepareAsync())
        {
            codeOnly.PrepareActivation(default);
            Assert.Equal(acceptedWire!.SchemaHash, f.Preparation.PendingWireProjection!.SchemaHash);
            codeOnly.Activate();
            codeOnly.RetirePrevious();
        }
        Assert.Equal(-77, Fixture.Read32(f.Context, max, "Added"));
        Assert.Equal((long)int.MaxValue, Fixture.Read64(f.Context, max, "Wide"));
        f.World.DelEntity(min);
        f.World.ReleaseDelEntityBufferAll();
        Assert.Equal(1, f.World.GetPool<DynamicComponentAnchor>().Count);
    }

    [Fact]
    public async Task SchemaReloadRejectsPendingDeletesUntilTheHostDrainsThem()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        await f.LoadAsync();
        f.Start();
        int deleted = f.Seed(12), kept = f.Seed(34);
        f.Replace(Changed);
        f.World.DelEntity(deleted);
        int queued = f.World.DelEntBufferCount;
        Assert.True(queued > 0);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => f.PrepareAsync());
        Assert.Contains("deletion", error.Message);
        Assert.Equal(queued, f.World.DelEntBufferCount);
        Assert.Null(f.Preparation.PendingWireProjection);

        f.World.ReleaseDelEntityBufferAll();
        using var candidate = await f.PrepareAsync();
        candidate.PrepareActivation(default);
        candidate.Activate();
        candidate.RetirePrevious();
        Assert.Equal(34L, Fixture.Read64(f.Context, kept, "Wide"));
        Assert.Equal(1, f.World.GetPool<DynamicComponentAnchor>().Count);
    }

    [Theory]
    [InlineData("missing", "Value")]
    [InlineData("strict-version", "version")]
    [InlineData("missing-new-rule", "Added")]
    [InlineData("coerced-default", "Added")]
    [InlineData("existing-default", "Keep")]
    [InlineData("narrow", "i32_to_i64")]
    [InlineData("duplicate", "Value")]
    [InlineData("drop-field", "Name")]
    [InlineData("drop-pool", Pool)]
    [InlineData("version", "version")]
    [InlineData("bad-default", "Added")]
    public async Task UnsafeMigrationIsRejectedWithoutChangingOldData(string mode, string diagnostic)
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        await f.LoadAsync();
        f.Start();
        int entity = f.Seed(1); // Narrowing must be rejected even though the only row would fit.
        var oldContext = f.Context;
        var oldWire = f.Preparation.AcceptedWireProjection;
        f.Replace(mode switch
        {
            "missing" => Changed.Replace(", convert = 'i32_to_i64'", ""),
            "narrow" => Changed.Replace("Wide = 'i64'", "Wide = 'i32'").Replace("Keep = 'i32'", "Keep = 'i8'"),
            "duplicate" => Changed.Replace("from = 'Name'", "from = 'Value'"),
            "drop-field" => Changed.Replace(", Renamed = 'i32'", "").Replace(", Renamed = { from = 'Name' }", ""),
            "drop-pool" => "return {}",
            "version" => Changed.Replace("migrate = { from = 1", "migrate = { from = 2"),
            "strict-version" => Changed.Replace("version = 2", "version = 1"),
            "missing-new-rule" => Changed.Replace(", Added = { default = -77 }", ""),
            "coerced-default" => Changed.Replace("default = -77", "default = '7'"),
            "existing-default" => Changed.Replace("fields = { Wide = {", "fields = { Keep = { default = 7 }, Wide = {"),
            "bad-default" => Changed.Replace("default = -77", "default = 0.5"),
            _ => throw new InvalidOperationException(),
        });
        var refusal = await Assert.ThrowsAsync<InvalidDataException>(() => f.PrepareAsync());
        Assert.Contains(diagnostic, refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(oldWire, f.Preparation.AcceptedWireProjection);
        Assert.Same(oldContext, f.Context);
        Assert.Equal(1, Fixture.Read32(oldContext, entity, "Value"));
        Assert.Null(f.Preparation.PendingWireProjection);
    }

    [Fact]
    public async Task NonparticipatingBorrowerAndMissingSourceRejectBeforePublication()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        await f.LoadAsync();
        f.Start();
        var basis = f.Preparation.AcceptedComposition;
        f.Replace(Changed);
        using (var host = f.Bindings.CreateRevision())
        {
            host.Prepare(new("borrow", [], [new(Pool)], []), "external-serializer");
            var borrow = await Assert.ThrowsAsync<InvalidDataException>(() => f.PrepareAsync());
            Assert.Contains("external-serializer", borrow.Message);
        }
        f.DeleteEntry();
        var missing = await Assert.ThrowsAsync<InvalidDataException>(() => f.PrepareAsync());
        Assert.Contains("main.lua", missing.Message);
        Assert.Same(basis, f.Preparation.AcceptedComposition);
    }
#endif

    [Fact]
    public async Task StandaloneReloadCannotUseAMigrationDeclaration()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        await f.LoadAsync();
        f.Start();
        int entity = f.Seed(5);
        var old = f.Context;
        var basis = f.Preparation.AcceptedComposition;
        f.Replace(Changed);
        var refusal = await Assert.ThrowsAsync<InvalidDataException>(async () => await f.Preparation.PrepareReloadAsync(default));
        Assert.Contains("standalone reload cannot change", refusal.Message);
        var oldWire = f.Preparation.AcceptedWireProjection;
        using (var apply = await f.Preparation.PrepareLiveAsync(basis!, basis!, default))
        {
            apply.PrepareActivation(default);
            Assert.Equal(oldWire!.SchemaHash, f.Preparation.PendingWireProjection!.SchemaHash);
        }
        Assert.Same(basis, f.Preparation.AcceptedComposition);
        Assert.Equal(5, Fixture.Read32(old, entity, "Value"));
#if !DEBUG
        await Assert.ThrowsAsync<NotSupportedException>(() => f.PrepareAsync());
#endif
    }

    private sealed class EmptyCompiledProjection : IRevisionReplicaProjection
    {
        public long SchemaHash => 1;
        public void Write(EcsWorld world, int entity, IWriter writer) { }
        public void Validate(IReader reader) { }
        public void Apply(EcsWorld world, int entity, IReader reader) { }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("karpik-mod-migration-").FullName;
        private readonly JobSystem _jobs = new(workerCount: 2, prefix: nameof(ModLiveMigrationTests));
        private readonly ComponentCallbackScope _scope = new();
        private readonly Application _application = new(Side.Server);
        private readonly ModsRegistry _registry;
        internal readonly ModsLifecycleManager Lifecycle;
        internal readonly EcsDefaultWorld World = new();
        internal readonly ComponentQueryPreparer Bindings;
        internal readonly LuaSimulationPreparation Preparation;
        private string EntryPath => Path.Combine(_root, "Mods", "probe", "Shared", "main.lua");
        internal LuaEcsContext Context => ((LuaScriptRuntime)Lifecycle.Containers[0].Runtime).GetContext(0);

        internal Fixture()
        {
            PhysicalFileSystem fs;
            string previous = Environment.CurrentDirectory;
            try { Environment.CurrentDirectory = _root; fs = new PhysicalFileSystem(); }
            finally { Environment.CurrentDirectory = previous; }
            Job.Initialize(_jobs);
            _registry = new ModsRegistry(fileSystem: fs);
            Lifecycle = new ModsLifecycleManager(new LuaScriptLoader(fs, NullLogger<IScriptLoader>.Instance));
            var components = new LuaComponentRegistry(new ClrComponentBindings([]));
            var schemas = new ComponentSchemaRegistry();
            Bindings = new ComponentQueryPreparer(World, components.Bindings, schemas, _scope);
            Preparation = new LuaSimulationPreparation(_registry, Lifecycle, _application, new SystemRegistry(), new Time(),
                components, schemas, Bindings, _scope, new ScriptSystemOptions(), NullLogger<IScriptLoader>.Instance);
        }

        internal async Task LoadAsync(string initial = Initial, bool sideEntry = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
            File.WriteAllText(Path.Combine(_root, "Mods", "probe", "mod_info.json"), """{"id":"probe"}""");
            if (sideEntry) WriteScript("Server", "main.lua", initial);
            else Replace(initial);
            var startup = new LuaModSetPreparation(_registry, _application);
            await startup.PrepareAsync(default);
            startup.CompleteStartupWithoutContent(default);
        }

        internal void Start()
        {
            Preparation.PrepareAsync(default).GetAwaiter().GetResult();
            Lifecycle.Containers[0].Start();
        }
        internal void Replace(string lua) => File.WriteAllText(EntryPath, lua);
        internal void DeleteEntry() => File.Delete(EntryPath);
        internal void WriteSideHelper(string lua) => WriteScript("Server", "proof_kind.lua", lua);
        internal void WriteScript(string slice, string file, string lua)
        {
            var dir = Path.Combine(_root, "Mods", "probe", slice);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file), lua);
        }
        internal void InstallUnselected()
        {
            string mod = Path.Combine(_root, "Mods", "unsolicited");
            Directory.CreateDirectory(Path.Combine(mod, "Shared"));
            File.WriteAllText(Path.Combine(mod, "mod_info.json"), """{"id":"unsolicited"}""");
            File.WriteAllText(Path.Combine(mod, "Shared", "main.lua"), "error('must not enter selected set')");
        }

        internal async Task<ISimulationCodeReloadCandidate> PrepareAsync()
        {
            var method = typeof(LuaSimulationPreparation).GetMethod("PrepareSchemaReloadAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(method is not null, "No coordinated Debug migration entry point.");
            var prepare = method!.CreateDelegate<Func<ModStartupDependencies, CancellationToken, ValueTask<ISimulationCodeReloadCandidate>>>(Preparation);
            return await prepare(Preparation.AcceptedComposition!, default);
        }

        internal RevisionReplicaBaseline Baseline(IRevisionReplicaProjection mod) => new(World, 1, new EmptyCompiledProjection(), mod);
        internal int Seed(int value, bool vector = false)
        {
            int entity = World.NewEntity();
            var context = Context;
            context.Enter();
            try
            {
                var pool = context.Queries[0].Components[Pool];
                var view = pool.Add(entity);
                view.WriteI32(Field(pool, "Value"), value);
                view.WriteI32(Field(pool, "Keep"), 3);
                view.WriteI32(Field(pool, "Name"), 16);
                if (vector)
                {
                    view.WriteI32(Field(pool, "Vector[1]"), 25);
                    view.WriteI32(Field(pool, "Vector[2]"), 31);
                }
            }
            finally { context.Exit(); }
            return entity;
        }
        internal static int Read32(LuaEcsContext context, int entity, string name)
        {
            context.Enter();
            try { var handle = context.Queries[0].Components[Pool]; return handle.Get(entity).ReadI32(Field(handle, name)); }
            finally { context.Exit(); }
        }
        internal static long Read64(LuaEcsContext context, int entity, string name)
        {
            context.Enter();
            try { var handle = context.Queries[0].Components[Pool]; return handle.Get(entity).ReadI64(Field(handle, name)); }
            finally { context.Exit(); }
        }
        private static int Field(PreparedComponentHandle pool, string name)
        {
            var fields = pool.Fields.Span;
            for (int i = 0; i < fields.Length; i++) if (fields[i].Name == name) return i;
            throw new InvalidOperationException(name);
        }

        public void Dispose()
        {
            Preparation.Dispose();
            Lifecycle.Dispose();
            Bindings.Dispose();
            _scope.Dispose();
            World.Destroy();
            Job.ShutdownIfCurrent(_jobs);
            Directory.Delete(_root, recursive: true);
        }
    }
}
