using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;

[Collection(nameof(WorkingDirectoryCollection))]
public sealed class ModLiveClrPoolTests
{
    public struct Health : IEcsComponent { public int Value; }

    private const string Component = "game:Health";
    private const string QueryMod = "health-query";
    private const string QuerySource = """
        return { systems = {{ id = 'run', phase = 'update',
          queries = { health = { inc = { ['game:Health'] = { readonly = false } } } },
          callback = function(dt, ctx)
            local q = ctx.queries.health
            for entity in q:entities() do
              local health = q.components['game:Health']:get(entity)
              health.Value = health.Value + 1
            end
          end }} }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UninitializedClrPool_IsNotInstalledOrAnnouncedByRejectedLiveCandidate(bool declareIdOnly)
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture(QuerySource, disabled: true);
        await f.LoadCompositionAsync();
        f.Start();
        if (declareIdOnly) f.World.GetComponentTypeID<Health>();
        var basis = f.Preparation.AcceptedComposition;
        int poolCount = f.World.PoolsCount;
        Assert.False(f.HasHealthPool);
        Assert.Equal(0, f.HealthPoolInitializations);

        var failure = await Record.ExceptionAsync(async () =>
        {
            // If preparation is wrongly accepted, discard it without publishing either revision.
            using var candidate = await f.PrepareLiveAsync();
            candidate.PrepareActivation(default);
        });

        Assert.Equal((false, 0, poolCount), (f.HasHealthPool, f.HealthPoolInitializations, f.World.PoolsCount));
        Assert.Same(basis, f.Preparation.AcceptedComposition);
        Assert.Contains(Component, Assert.IsType<InvalidDataException>(failure).Message);
        Assert.Contains("startup", failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ColdInitializedClrPool_SupportsLiveQueryAfterAbortAndCommitWithoutAnotherInit()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture(QuerySource, disabled: true);
        var pool = f.World.GetPool<Health>();
        int entity = f.World.NewEntity();
        pool.Add(entity).Value = 41;
        await f.LoadCompositionAsync();
        f.Start();
        var basis = f.Preparation.AcceptedComposition;

        using (var aborted = await f.PrepareLiveAsync()) aborted.PrepareActivation(default);
        Assert.Same(basis, f.Preparation.AcceptedComposition);
        Assert.Equal(41, pool.Read(entity).Value);

        using var candidate = await f.PrepareLiveAsync();
        candidate.PrepareActivation(default);
        candidate.Activate();
        candidate.RetirePrevious();
        Assert.Single(f.Preparation.ActiveSystems.ToArray()).Update();
        Assert.Equal(42, pool.Read(entity).Value);
        Assert.Equal(1, f.HealthPoolInitializations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupAndStandaloneReload_StillLazilyInitializeClrPools(bool standaloneReload)
    {
        using var f = new Fixture(standaloneReload ? "return {}" : QuerySource);
        await f.LoadCompositionAsync();
        Assert.False(f.HasHealthPool);
        f.Start();
        if (standaloneReload)
        {
            Assert.False(f.HasHealthPool);
            f.WriteSource(QuerySource);
            using var candidate = await f.Preparation.PrepareReloadAsync(default);
            candidate.PrepareActivation(default);
            candidate.Activate();
            candidate.RetirePrevious();
        }

        Assert.True(f.HasHealthPool);
        Assert.Equal(1, f.HealthPoolInitializations);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("karpik-live-clr-").FullName;
        private readonly JobSystem _jobs = new(workerCount: 2, prefix: nameof(ModLiveClrPoolTests));
        private readonly ComponentCallbackScope _scope = new();
        private readonly Application _application = new(Side.Server);
        private readonly ModsRegistry _registry;
        private readonly ModsLifecycleManager _lifecycle;
        private readonly ComponentQueryPreparer _bindings;
        private readonly string _entry;
        internal readonly EcsDefaultWorld World = new();
        internal readonly LuaSimulationPreparation Preparation;
        internal int HealthPoolInitializations { get; private set; }
        internal bool HasHealthPool => World.IsComponentTypeDeclared<Health>() && World.TryFindPoolInstance(typeof(Health), out _);

        internal Fixture(string source, bool disabled = false)
        {
            var mod = Path.Combine(_root, "Mods", QueryMod);
            Directory.CreateDirectory(Path.Combine(mod, "Shared"));
            File.WriteAllText(Path.Combine(mod, "mod_info.json"), $$"""{"id":"{{QueryMod}}"}""");
            if (disabled) File.WriteAllText(Path.Combine(_root, "mods.json"), $$"""{"disabled":["{{QueryMod}}"]}""");
            _entry = Path.Combine(mod, "Shared", "main.lua");
            WriteSource(source);

            string previous = Environment.CurrentDirectory;
            PhysicalFileSystem fs;
            try { Environment.CurrentDirectory = _root; fs = new PhysicalFileSystem(); }
            finally { Environment.CurrentDirectory = previous; }
            Job.Initialize(_jobs);
            var logger = NullLogger<IScriptLoader>.Instance;
            _registry = new ModsRegistry(fileSystem: fs);
            _lifecycle = new ModsLifecycleManager(new LuaScriptLoader(fs, logger));
            var components = new LuaComponentRegistry(new ClrComponentBindings([new GeneratedBindings()]));
            components.Register<Health>(Component);
            var schemas = new ComponentSchemaRegistry();
            _bindings = new ComponentQueryPreparer(World, components.Bindings, schemas, _scope);
            Preparation = new LuaSimulationPreparation(_registry, _lifecycle, _application,
                new SystemRegistry(), new Time(), components, schemas, _bindings, _scope, new ScriptSystemOptions(), logger);
            World.OnPoolInitialized += pool => { if (pool.ComponentType == typeof(Health)) HealthPoolInitializations++; };
        }

        internal void WriteSource(string source) => File.WriteAllText(_entry, source);

        internal async Task LoadCompositionAsync()
        {
            var startup = new LuaModSetPreparation(_registry, _application);
            await startup.PrepareAsync(default);
            startup.CompleteStartupWithoutContent(default);
        }

        internal void Start() => Preparation.PrepareAsync(default).GetAwaiter().GetResult();

        internal ValueTask<ISimulationCodeReloadCandidate> PrepareLiveAsync()
        {
            var basis = Preparation.AcceptedComposition!;
            var target = ModsRegistry.ComposeSelection(_registry.DiscoverSelection(basis, [QueryMod]), []);
            return Preparation.PrepareLiveAsync(basis, target, default);
        }

        public void Dispose()
        {
            Preparation.Dispose();
            _lifecycle.Dispose();
            _bindings.Dispose();
            _scope.Dispose();
            World.Destroy();
            Job.ShutdownIfCurrent(_jobs);
            Directory.Delete(_root, recursive: true);
        }
    }
}
