using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Content.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;
using Xunit;

[Collection(nameof(WorkingDirectoryCollection))]
public sealed class ModLiveBindingsTests
{
    private const string Pool = "provider:State";
    private const string Provider = """
        print('provider:top')
        return {
          components = { State = { version = 1, fields = { Value = 'i32' } } },
          on_load = function() print('provider:load') end,
          on_start = function() print('provider:start') end,
          on_reload = function() print('provider:reload') end,
          on_destroy = function() print('provider:destroy') end,
          on_unload = function() print('provider:unload') end,
          systems = {{ id = 'run', phase = 'update', callback = function() end,
            queries = { access = { opt = { ['provider:State'] = { readonly = false } } } } }}
        }
        """;

    [Fact]
    public async Task RowsRejectRemovalUntilDeletionIsReleased_ThenReenableDoesNotReviveHandles()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Write("provider", Provider);
        await f.LoadCompositionAsync();
        f.Start();
        var old = Assert.Single(f.Lifecycle.Containers);
        var context = ((LuaScriptRuntime)old.Runtime).GetContext(0);
        var oldQuery = Assert.Single(context.Queries);
        var oldHandle = oldQuery.Components[Pool];
        int entity = f.World.NewEntity();
        using (var seed = f.Bindings.CreateRevision())
        {
            var handle = BindHost(seed);
            seed.ActivateRevision();
            seed.CallbackScope.Enter("codec");
            handle.Add(entity).WriteI32(0, 42);
            seed.CallbackScope.Exit();
        }
        old.Start();

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => f.PrepareAsync());
        Assert.Contains(Pool, failure.Message);
        Assert.Contains("live", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(old, Assert.Single(f.Lifecycle.Containers));
        context.Enter();
        try { Assert.Equal(42, oldHandle.Get(entity).ReadI32(0)); }
        finally { context.Exit(); }
        Assert.DoesNotContain(f.Events, e => e.EndsWith("provider:destroy"));

        f.World.DelEntity(entity); // Rows remain until the Dragon deletion buffer is released.
        using (var remove = await f.PrepareAsync())
        {
            remove.PrepareActivation(default);
            remove.Activate();
            Assert.Empty(f.Lifecycle.Containers);
            Assert.Throws<ObjectDisposedException>(context.Enter);
            remove.RetirePrevious();
        }
        using (var enable = await f.PrepareAsync("provider"))
        {
            enable.PrepareActivation(default);
            enable.Activate();
            Assert.Equal(1, f.Count("provider:start"));
            enable.RetirePrevious();
        }
        Assert.Equal(2, f.Count("provider:start"));
        Assert.Equal(1, f.Count("provider:destroy"));
        Assert.Equal(1, f.Count("provider:unload"));
        Assert.Equal(0, f.Count("provider:reload"));
        var fresh = ((LuaScriptRuntime)Assert.Single(f.Lifecycle.Containers).Runtime).GetContext(0);
        Assert.NotSame(oldHandle, Assert.Single(fresh.Queries).Components[Pool]);
        fresh.Enter();
        try
        {
            Assert.Throws<ObjectDisposedException>(oldQuery.Capture);
            Assert.Throws<ObjectDisposedException>(() => oldHandle.Has(entity));
        }
        finally { fresh.Exit(); }
    }

    [Theory]
    [InlineData("opt")]
    [InlineData("exc")]
    public async Task EmptyPoolWithSurvivingQueryIsRejected(string group)
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Write("provider", Provider);
        f.Write("survivor", $$$"""
            return { systems = {{ id = 'run', phase = 'update', callback = function() end,
                queries = { access = { {{{group}}} = { ['provider:State'] = {} } } } }} }
            """);
        await f.LoadCompositionAsync();
        f.Start();
        var original = f.Lifecycle.Containers.ToArray();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => f.PrepareAsync("survivor"));
        Assert.Contains(Pool, error.Message);
        Assert.Contains("survivor", error.Message);
        Assert.Equal(original, f.Lifecycle.Containers);
        f.Preparation.ActiveSystems.Span[1].Update();
    }

    [Fact]
    public async Task TrackedHostBindingBlocksRemovalAndCannotLeakRawFieldCapability()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Write("provider", Provider);
        await f.LoadCompositionAsync();
        f.Start();
        using var codec = f.Bindings.CreateRevision();
        var handle = BindHost(codec);
        codec.ActivateRevision();
        var field = handle.Fields.Span[0].I32;
        Assert.Throws<InvalidOperationException>(() => field.Write(1, 5));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => f.PrepareAsync());
        Assert.Contains(Pool, error.Message);
        Assert.Contains("codec", error.Message);
        codec.Dispose();
        Assert.Throws<ObjectDisposedException>(() => field.Write(1, 5));
        using var candidate = await f.PrepareAsync();
        candidate.PrepareActivation(default);
        candidate.Activate();
        candidate.RetirePrevious();
    }

    [Fact]
    public async Task OnlyChangedDependencyRebuildsFromAcceptedSource_UnchangedVmAndBindingsSurvive()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Disable("provider");
        f.Write("provider", Provider);
        f.Write("consumer", """
            local present = has_dependency('provider')
            print('consumer:top:' .. tostring(present))
            return {
              on_load = function() print('consumer:load') end,
              on_start = function() print('consumer:start') end,
              on_reload = function() assert(present); print('consumer:reload') end,
              on_destroy = function() print('consumer:destroy') end,
              on_unload = function() print('consumer:unload') end
            }
            """, """{"id":"consumer","dependencies":{"shared":[{"id":"provider","optional":true}]}}""");
        f.Write("stable", """
            local ticks = 0
            return {
              components = { State = { version = 1, fields = { Value = 'i32' } } },
              on_start = function() print('stable:start') end,
              on_destroy = function() print('stable:destroy') end,
              systems = {{ id = 'run', phase = 'update', queries = { access = { opt = { ['stable:State'] = {} } } },
                callback = function(dt, ctx) ticks = ticks + 1; print('stable:' .. ticks) end }}
            }
            """);
        await f.LoadCompositionAsync();
        f.Start();
        var consumer = f.Lifecycle.Containers[0];
        var stable = f.Lifecycle.Containers[1];
        var stableAdapter = Assert.Single(f.Preparation.ActiveSystems.ToArray());
        consumer.Start();
        stableAdapter.Update();
        var context = ((LuaScriptRuntime)stable.Runtime).GetContext(0);
        var query = Assert.Single(context.Queries);
        f.RewriteEntry("consumer", "error('do not reopen accepted survivor')");

        using var candidate = await f.PrepareAsync("consumer", "provider", "stable");
        Assert.Equal(1, f.Count("consumer:top:true"));
        Assert.Equal(1, f.Count("provider:load"));
        Assert.Equal(0, f.Count("consumer:reload"));
        candidate.PrepareActivation(default);
        Assert.Equal(1, f.Count("consumer:reload"));
        Assert.Equal(0, f.Count("provider:start"));
        Assert.Equal(0, f.Count("provider:reload"));
        Assert.Same(consumer, f.Lifecycle.Containers[0]);
        candidate.Activate();
        Assert.Equal(0, f.Count("provider:start"));
        candidate.RetirePrevious();

        var owners = f.Lifecycle.Containers.ToDictionary(owner => owner.ModDefinition.MetaData.Id);
        Assert.Same(stable, owners["stable"]);
        Assert.NotSame(consumer, owners["consumer"]);
        Assert.Contains(stableAdapter, f.Preparation.ActiveSystems.ToArray());
        Assert.Same(query, Assert.Single(((LuaScriptRuntime)stable.Runtime).GetContext(0).Queries));
        Assert.Equal(1, f.Count("consumer:start"));
        Assert.Equal(1, f.Count("consumer:destroy"));
        Assert.Equal(1, f.Count("consumer:unload"));
        Assert.Equal(1, f.Count("stable:start"));
        Assert.Equal(0, f.Count("stable:destroy"));
        Assert.Equal(1, f.Count("provider:start"));
        stableAdapter.Update();
        Assert.Equal(1, f.Count("stable:2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyRefreshCannotAddOrRemoveASurvivorsSchema(bool initiallyPresent)
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Disable("provider");
        f.Write("provider", Provider);
        f.Write("consumer", $$$"""
            local declare = has_dependency('provider') ~= {{{(initiallyPresent ? "true" : "false")}}}
            return { components = declare and { Local = { version = 1, fields = { Value = 'i32' } } } or nil,
              on_reload = function() print('consumer:reload') end }
            """, """{"id":"consumer","dependencies":{"shared":[{"id":"provider","optional":true}]}}""");
        await f.LoadCompositionAsync();
        f.Start();
        var basis = f.Preparation.AcceptedComposition;
        var old = Assert.Single(f.Lifecycle.Containers);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => f.PrepareAsync("consumer", "provider"));

        Assert.Contains("consumer", error.Message);
        Assert.Same(basis, f.Preparation.AcceptedComposition);
        Assert.Same(old, Assert.Single(f.Lifecycle.Containers));
        Assert.Equal(0, f.Count("consumer:reload"));
    }

    [Fact]
    public async Task SequentialScratchRejectsReentryAndStaleTokensAcrossVms()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Write("provider", Provider);
        f.Write("second", Provider.Replace("provider:", "second:"));
        await f.LoadCompositionAsync();
        f.Start();
        var first = ((LuaScriptRuntime)f.Lifecycle.Containers[0].Runtime).GetContext(0);
        var second = ((LuaScriptRuntime)f.Lifecycle.Containers[1].Runtime).GetContext(0);
        var field = typeof(LuaEcsContext).GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var arena = Assert.IsType<StructuralCommandBuffer>(field.GetValue(first));
        Assert.Same(arena, field.GetValue(second));
        var firstPool = Assert.Single(first.Queries).Components[Pool];
        var secondPool = Assert.Single(second.Queries).Components["second:State"];

        PendingEntity token;
        first.Enter();
        try
        {
            token = arena.Create();
            arena.Add(firstPool, token);
            Assert.Throws<InvalidOperationException>(second.Enter);
            first.Complete();
        }
        finally { first.Exit(); }
        Assert.Equal(1, f.World.Count);
        second.Enter();
        try
        {
            Assert.Throws<InvalidOperationException>(() => arena.Add(secondPool, token));
            Assert.Throws<InvalidOperationException>(second.Complete);
        }
        finally { second.Exit(); }
        Assert.Equal(1, f.World.Count);
    }

    [Fact]
    public async Task ReadyCandidateCannotPublishFromAnotherThreadOrAfterWorldResumes()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Write("provider", Provider);
        await f.LoadCompositionAsync();
        f.Start();
        var old = Assert.Single(f.Lifecycle.Containers);
        using var candidate = await f.PrepareAsync();
        candidate.PrepareActivation(default);
        Exception? offOwner = null;
        var other = new Thread(() => offOwner = Record.Exception(candidate.Activate));
        other.Start();
        Assert.True(other.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(offOwner);
        Assert.Same(old, Assert.Single(f.Lifecycle.Containers));

        int entity = f.World.NewEntity();
        var context = ((LuaScriptRuntime)old.Runtime).GetContext(0);
        context.Enter();
        try { Assert.Single(context.Queries).Components[Pool].Add(entity); }
        finally { context.Exit(); }
        Assert.Throws<InvalidOperationException>(candidate.Activate);
        Assert.Same(old, Assert.Single(f.Lifecycle.Containers));
        Assert.Equal(1, f.World.Count);
    }

    [Fact]
    public async Task ADeletionPlaybackExceptionRequiresFailStopBeforePublication()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Write("provider", Provider);
        await f.LoadCompositionAsync();
        f.Start();
        var old = Assert.Single(f.Lifecycle.Containers);
        var context = ((LuaScriptRuntime)old.Runtime).GetContext(0);
        int entity = f.World.NewEntity();
        context.Enter();
        try { Assert.Single(context.Queries).Components[Pool].Add(entity); }
        finally { context.Exit(); }
        f.World.DelEntity(entity);
        var listener = new FailingDeletionListener();
        f.World.AddListener(listener);
        try
        {
            var error = await Assert.ThrowsAsync<SimulationCodeReloadCleanupException>(() => f.PrepareAsync());
            Assert.Contains("playback-failed", error.ToString());
            Assert.Same(old, Assert.Single(f.Lifecycle.Containers));
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.PrepareAsync());
        }
        finally { f.World.RemoveListener(listener); }
    }

    [Fact]
    public async Task WarmedPublicationDoesNotAllocateOrInvokeLua()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Write("provider", Provider);
        await f.LoadCompositionAsync();
        f.Start();
        for (int i = 0; i < 8; i++)
        {
            using var candidate = await f.PrepareAsync(i % 2 == 0 ? [] : ["provider"]);
            candidate.PrepareActivation(default);
            int eventCount = f.Events.Count;
            long before = GC.GetAllocatedBytesForCurrentThread();
            candidate.Activate();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (i > 1) Assert.Equal(0, allocated);
            Assert.Equal(eventCount, f.Events.Count);
            candidate.RetirePrevious();
        }
    }

    [Fact]
    public async Task NewStartFailureOccursAfterPublishAndCannotRollBack()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var f = new Fixture();
        f.Disable("provider");
        f.Write("provider", Provider.Replace("print('provider:start')", "error('new-start-failed')"));
        await f.LoadCompositionAsync();
        f.Start();
        var basis = f.Preparation.AcceptedComposition;
        using var candidate = await f.PrepareAsync("provider");
        candidate.PrepareActivation(default);
        candidate.Activate();
        var committed = f.Preparation.AcceptedComposition;
        Assert.NotSame(basis, committed);
        Assert.ThrowsAny<InterpreterException>(candidate.RetirePrevious);
        Assert.Same(committed, f.Preparation.AcceptedComposition);
        Assert.Single(f.Lifecycle.Containers);
    }

    private static PreparedComponentHandle BindHost(ComponentQueryPreparer domain) =>
        domain.Prepare(new("host", [], [], [new(Pool, false)]), "codec").Components[Pool];

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("karpik-live-bindings-").FullName;
        private readonly JobSystem _jobs = new(workerCount: 2, prefix: nameof(ModLiveBindingsTests));
        private readonly ComponentCallbackScope _scope = new();
        private readonly ModsRegistry _registry;
        private readonly Application _application = new(Side.Server);
        internal readonly EcsDefaultWorld World = new();
        internal readonly ComponentQueryPreparer Bindings;
        internal readonly ModsLifecycleManager Lifecycle;
        internal readonly LuaSimulationPreparation Preparation;
        internal readonly List<string> Events = [];

        internal Fixture()
        {
            string previous = Environment.CurrentDirectory;
            PhysicalFileSystem fs;
            try { Environment.CurrentDirectory = _root; fs = new PhysicalFileSystem(); }
            finally { Environment.CurrentDirectory = previous; }
            Job.Initialize(_jobs);
            _registry = new ModsRegistry(fileSystem: fs);
            var logger = new Logger(Events);
            Lifecycle = new ModsLifecycleManager(new LuaScriptLoader(fs, logger));
            var components = new LuaComponentRegistry(new ClrComponentBindings([]));
            var schemas = new ComponentSchemaRegistry();
            Bindings = new ComponentQueryPreparer(World, components.Bindings, schemas, _scope);
            Preparation = new LuaSimulationPreparation(_registry, Lifecycle, _application,
                new SystemRegistry(), new Time(), components, schemas, Bindings, _scope, new ScriptSystemOptions(), logger);
        }

        internal void Disable(string id) => File.WriteAllText(Path.Combine(_root, "mods.json"), $$"""{"disabled":["{{id}}"]}""");
        internal void RewriteEntry(string id, string source) => File.WriteAllText(Path.Combine(_root, "Mods", id, "Shared", "main.lua"), source);
        internal void Write(string id, string source, string? manifest = null)
        {
            string mod = Path.Combine(_root, "Mods", id);
            Directory.CreateDirectory(Path.Combine(mod, "Shared"));
            File.WriteAllText(Path.Combine(mod, "mod_info.json"), manifest ?? $$"""{"id":"{{id}}"}""");
            RewriteEntry(id, source);
        }
        internal int Count(string suffix) => Events.Count(message => message.EndsWith(suffix, StringComparison.Ordinal));

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
            Lifecycle.Dispose();
            Bindings.Dispose();
            _scope.Dispose();
            World.Destroy();
            Job.ShutdownIfCurrent(_jobs);
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FailingDeletionListener : IEcsWorldEventListener
    {
        public void OnWorldResize(int newSize) { }
        public void OnReleaseDelEntityBuffer(ReadOnlySpan<int> buffer) => throw new InvalidOperationException("playback-failed");
        public void OnWorldDestroy() { }
    }

    private sealed class Logger(List<string> events) : ILogger<IScriptLoader>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => events.Add(formatter(state, exception));
    }
}
