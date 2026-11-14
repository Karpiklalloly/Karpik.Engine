using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;
using Xunit;

public sealed class LuaHotReloadLifecycleTests
{
    [Fact]
    public async Task Candidate_PublishesAllMods_AfterReloadHooks_WithoutStartingAgain()
    {
        using var f = new Fixture("a", "b");
        await f.StartAsync();
        foreach (ModContainer container in f.Lifecycle.Containers) container.Start();
        ModContainer[] original = f.Lifecycle.Containers.ToArray();
        var participant = Assert.IsAssignableFrom<ISimulationCodeReloadParticipant>(f.Preparation);
        f.Loader.Sources["a"] = Source("a", "new");
        f.Loader.Sources["b"] = Source("b", "new");
        using ISimulationCodeReloadCandidate candidate = await participant.PrepareReloadAsync(default);
        Assert.Equal(original, f.Lifecycle.Containers);
        Assert.Equal(2, candidate.Systems.Length);
        Assert.DoesNotContain("a:new:reload", f.Events);
        candidate.PrepareActivation(default);
        Assert.Contains("a:new:reload", f.Events);
        Assert.Contains("b:new:reload", f.Events);
        Assert.Equal(original, f.Lifecycle.Containers);
        candidate.Activate();
        candidate.RetirePrevious();
        candidate.Dispose(); // Ownership was transferred: this must not destroy the active revision.
        foreach (ISystemUpdate system in participant.ActiveSystems.ToArray()) system.Update();
        Assert.Contains("a:new:update:true", f.Events);
        Assert.Contains("b:new:update:true", f.Events);
        Assert.DoesNotContain("a:new:start", f.Events);
        Assert.DoesNotContain("b:new:start", f.Events);
        Assert.Equal(1, f.Events.Count(e => e == "a:old:destroy"));
        Assert.Equal(1, f.Events.Count(e => e == "a:old:unload"));
        Assert.Throws<ObjectDisposedException>(() => original[0].Update(0.02));
    }

    [Fact]
    public async Task RejectedCandidate_UnloadsWithoutDestroy_AndKeepsOldVersionActive()
    {
        using var f = new Fixture("a");
        await f.StartAsync();
        var old = Assert.Single(f.Lifecycle.Containers);
        old.Start();
        var participant = Assert.IsAssignableFrom<ISimulationCodeReloadParticipant>(f.Preparation);
        f.Loader.Sources["a"] = Source("a", "new");
        using (ISimulationCodeReloadCandidate candidate = await participant.PrepareReloadAsync(default))
        {
            candidate.PrepareActivation(default);
        }
        Assert.Same(old, Assert.Single(f.Lifecycle.Containers));
        Assert.Equal(1, f.Events.Count(e => e == "a:new:unload"));
        Assert.DoesNotContain("a:new:destroy", f.Events);
        Assert.DoesNotContain("a:old:destroy", f.Events);
        participant.ActiveSystems.Span[0].Update();
        Assert.Contains("a:old:update:false", f.Events);
    }

    [Fact]
    public async Task SchemaChange_RejectsWholeCandidate_BeforeReloadHook()
    {
        using var f = new Fixture("a", "b");
        await f.StartAsync();
        ModContainer[] original = f.Lifecycle.Containers.ToArray();
        var participant = Assert.IsAssignableFrom<ISimulationCodeReloadParticipant>(f.Preparation);
        f.Loader.Sources["a"] = Source("a", "new");
        f.Loader.Sources["b"] = Source("b", "new").Replace("version = 1", "version = 2");
        await Assert.ThrowsAsync<InvalidDataException>(async () => await participant.PrepareReloadAsync(default));
        Assert.Equal(original, f.Lifecycle.Containers);
        Assert.DoesNotContain("a:new:reload", f.Events);
        Assert.Contains("a:new:unload", f.Events);
        Assert.Contains("b:new:unload", f.Events);
    }

    [Fact]
    public async Task FailingLoadHook_AttemptsUnload_AndPreservesAllActiveMods()
    {
        using var f = new Fixture("a", "b");
        await f.StartAsync();
        ModContainer[] original = f.Lifecycle.Containers.ToArray();
        var participant = Assert.IsAssignableFrom<ISimulationCodeReloadParticipant>(f.Preparation);
        f.Loader.Sources["a"] = Source("a", "new");
        f.Loader.Sources["b"] = Source("b", "new").Replace("print('b:new:load')", "print('b:new:load'); error('load-failed')");
        await Assert.ThrowsAnyAsync<InterpreterException>(async () => await participant.PrepareReloadAsync(default));
        Assert.Equal(original, f.Lifecycle.Containers);
        Assert.Contains("a:new:unload", f.Events);
        Assert.Contains("b:new:unload", f.Events);
        Assert.DoesNotContain("a:new:destroy", f.Events);
        Assert.DoesNotContain("b:new:destroy", f.Events);
    }

    [Fact]
    public async Task ReloadHookFailure_IsRejectable_AndCleanupLuaErrorsDoNotHideIt()
    {
        using var f = new Fixture("a");
        await f.StartAsync();
        var participant = Assert.IsAssignableFrom<ISimulationCodeReloadParticipant>(f.Preparation);
        f.Loader.Sources["a"] = Source("a", "new")
            .Replace("reloaded = true", "error('reload-failed')")
            .Replace("print('a:new:unload')", "print('a:new:unload'); error('unload-failed')");
        ISimulationCodeReloadCandidate candidate = await participant.PrepareReloadAsync(default);
        Assert.ThrowsAny<InterpreterException>(() => candidate.PrepareActivation(default));
        candidate.Dispose();
        candidate.Dispose();
        Assert.Equal(1, f.Events.Count(e => e == "a:new:unload"));
        Assert.NotEmpty(f.Logger.Errors);
        participant.ActiveSystems.Span[0].Update();
        Assert.Contains("a:old:update:false", f.Events);
    }

    [Fact]
    public async Task Retirement_LogsLuaHookErrors_ContinuesCleanup_AndPreservesDisabledState()
    {
        using var f = new Fixture("a", "b");
        f.Loader.Sources["a"] = Source("a", "old").Replace("print('a:old:destroy')", "print('a:old:destroy'); error('destroy-failed')")
            .Replace("print('a:old:unload')", "print('a:old:unload'); error('unload-failed')");
        await f.StartAsync();
        foreach (var container in f.Lifecycle.Containers) container.Start();
        f.Lifecycle.Containers[0].IsEnabled = false;
        var participant = Assert.IsAssignableFrom<ISimulationCodeReloadParticipant>(f.Preparation);
        f.Loader.Sources["a"] = Source("a", "new");
        f.Loader.Sources["b"] = Source("b", "new");
        using ISimulationCodeReloadCandidate candidate = await participant.PrepareReloadAsync(default);
        candidate.PrepareActivation(default);
        candidate.Activate();
        candidate.RetirePrevious();
        candidate.RetirePrevious();
        Assert.False(f.Lifecycle.Containers[0].IsEnabled);
        Assert.Equal(1, f.Events.Count(e => e == "a:old:destroy"));
        Assert.Equal(1, f.Events.Count(e => e == "a:old:unload"));
        Assert.Equal(1, f.Events.Count(e => e == "b:old:unload"));
        Assert.Equal(2, f.Logger.Errors.Count);
        foreach (ISystemUpdate system in participant.ActiveSystems.ToArray()) system.Update();
        Assert.DoesNotContain("a:new:update:true", f.Events);
        Assert.Contains("b:new:update:true", f.Events);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("42")]
    [InlineData("'reload'")]
    public void OnReload_RequiresAnOptionalFunction(string value)
    {
        var script = new Script();
        Table entry = script.DoString("return { on_reload = " + value + " }").Table;
        Assert.Throws<InvalidDataException>(() => new LuaScriptRuntime(script, [entry], "a", ["Shared/main.lua"]));
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1031", Justification = "The test owns and pumps its main-thread scheduler and observes completed loader tasks.")]
    public void AsyncLoader_WithScheduler_KeepsEveryTopLevelAndLoadHookOnOwner()
    {
        int ownerThread = Environment.CurrentManagedThreadId;
        using var scheduler = new MainThreadScheduler(ownerThread, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        using var f = new Fixture(scheduler, "a", "b");
        f.StartAsync().GetAwaiter().GetResult();
        var participant = Assert.IsAssignableFrom<ISimulationCodeReloadParticipant>(f.Preparation);
        f.Loader.Sources["a"] = Source("a", "new");
        f.Loader.Sources["b"] = "print('b:new:top-level')\n" + Source("b", "new");
        f.Loader.DelayNextLoad = true;
        Task<ISimulationCodeReloadCandidate> preparing = participant.PrepareReloadAsync(default).AsTask();
        Assert.False(preparing.IsCompleted);
        var completion = new Thread(f.Loader.CompleteDelayedLoad);
        completion.Start();
        Assert.True(completion.Join(TimeSpan.FromSeconds(5)));
        scheduler.RunUntilCompleted(preparing);
        using ISimulationCodeReloadCandidate candidate = preparing.GetAwaiter().GetResult();
        Assert.All(f.Loader.InvocationThreads, thread => Assert.Equal(ownerThread, thread));
        Assert.All(f.Loader.EventThreads, thread => Assert.Equal(ownerThread, thread));
        Assert.Contains("b:new:top-level", f.Events);
        Assert.Contains("a:new:load", f.Events);
        Assert.Contains("b:new:load", f.Events);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1031", Justification = "The test owns and pumps its main-thread scheduler and observes completed loader tasks.")]
    public void AsyncLoader_WithoutScheduler_RetainsCleanupForOwnerTeardown_AndNeverRunsOffOwnerHooks()
    {
        int ownerThread = Environment.CurrentManagedThreadId;
        using var f = new Fixture("a");
        f.StartAsync().GetAwaiter().GetResult();
        var participant = Assert.IsAssignableFrom<ISimulationCodeReloadParticipant>(f.Preparation);
        var runtime = new TrackingRuntime();
        f.Loader.NextRuntime = runtime;
        f.Loader.DelayNextLoad = true;
        Task<ISimulationCodeReloadCandidate> preparing = participant.PrepareReloadAsync(default).AsTask();
        var completion = new Thread(f.Loader.CompleteDelayedLoad);
        completion.Start();
        Assert.True(completion.Join(TimeSpan.FromSeconds(5)));
        Assert.Throws<SimulationCodeReloadCleanupException>(() => preparing.GetAwaiter().GetResult());
        Assert.Equal(0, runtime.LoadCount);
        Assert.Equal(0, runtime.DisposeCount);
        f.Dispose();
        Assert.Equal(1, runtime.DisposeCount);
        Assert.Equal(ownerThread, runtime.DisposalThread);
    }

    [Fact]
    public async Task InterpreterException_FromHostDispose_IsFatal_NotALoggableLuaHookError()
    {
        using var f = new Fixture("a");
        await f.StartAsync();
        var participant = Assert.IsAssignableFrom<ISimulationCodeReloadParticipant>(f.Preparation);
        // This non-Lua replacement also removes the schema, forcing rejection after Load.
        // Runtime.Dispose is a host operation even when its exception happens to be a Lua exception type.
        var runtime = new TrackingRuntime { ThrowOnDispose = true };
        f.Loader.NextRuntime = runtime;
        Exception error = await Assert.ThrowsAsync<SimulationCodeReloadCleanupException>(
            async () => await participant.PrepareReloadAsync(default));
        Assert.Contains("host-dispose-failed", error.ToString());
        Assert.Equal(1, runtime.DisposeCount);
        Assert.Empty(f.Logger.Errors);
    }

    [Fact]
    public void SharedCleanupFailure_DoesNotSkipSideCleanupCallbacks()
    {
        var events = new List<string>();
        var script = new Script();
        script.Options.DebugPrint = events.Add;
        Table shared = script.DoString("return { on_destroy = function() print('shared'); error('failed') end }").Table;
        Table side = script.DoString("return { on_destroy = function() print('side') end }").Table;
        using var runtime = new LuaScriptRuntime(script, [shared, side], "a", ["Shared/main.lua", "Server/main.lua"]);
        Assert.ThrowsAny<InterpreterException>(runtime.Destroy);
        Assert.Equal(["shared", "side"], events);
    }

    [Fact]
    public async Task CleanupLoggingFailure_DoesNotSkipOtherRuntimeResources()
    {
        using var f = new Fixture("a", "b");
        f.Loader.Sources["b"] = Source("b", "old").Replace(
            "print('b:old:destroy')", "print('b:old:destroy'); error('old-hook-failed')");
        await f.StartAsync();
        ModContainer[] old = f.Lifecycle.Containers.ToArray();
        foreach (var container in old) container.Start();
        f.Loader.Sources["a"] = Source("a", "new");
        f.Loader.Sources["b"] = Source("b", "new");
        var participant = Assert.IsAssignableFrom<ISimulationCodeReloadParticipant>(f.Preparation);
        using var candidate = await participant.PrepareReloadAsync(default);
        candidate.PrepareActivation(default);
        candidate.Activate();
        f.Logger.ThrowOnError = true;
        Assert.Throws<SimulationCodeReloadCleanupException>(candidate.RetirePrevious);
        Assert.Contains("a:old:unload", f.Events);
        Assert.Contains("b:old:unload", f.Events);
        Assert.All(old, owner => Assert.Throws<ObjectDisposedException>(() => owner.Update(.02)));
    }

    private static string Source(string id, string revision) => $$"""
        local reloaded = false
        return {
          components = { Temperature = { version = 1, fields = { Value = 'f32' } } },
          on_load = function() print('{{id}}:{{revision}}:load') end,
          on_start = function() print('{{id}}:{{revision}}:start') end,
          on_reload = function() print('{{id}}:{{revision}}:reload'); reloaded = true end,
          on_destroy = function() print('{{id}}:{{revision}}:destroy') end,
          on_unload = function() print('{{id}}:{{revision}}:unload') end,
          systems = { { id = 'run', phase = 'update', callback = function(dt)
            print('{{id}}:{{revision}}:update:' .. tostring(reloaded))
          end } }
        }
        """;

    private sealed class Fixture : IDisposable
    {
        internal readonly List<string> Events = [];
        internal readonly Logger Logger = new();
        internal readonly Loader Loader;
        internal readonly ModsLifecycleManager Lifecycle;
        internal readonly LuaSimulationPreparation Preparation;
        private readonly EcsDefaultWorld _world = new();
        private readonly ComponentCallbackScope _scope = new();
        private readonly ComponentQueryPreparer _queries;

        internal Fixture(params string[] ids) : this(null, ids) { }

        internal Fixture(MainThreadScheduler? scheduler, params string[] ids)
        {
            Loader = new Loader(Events);
            ModDefinition[] definitions = ids.Select(id => new ModDefinition(new ModMetaData(id, id, "test", "1", ""), id)).ToArray();
            foreach (string id in ids) Loader.Sources[id] = Source(id, "old");
            Lifecycle = new ModsLifecycleManager(Loader);
            var components = new LuaComponentRegistry(new ClrComponentBindings([]));
            var schemas = new ComponentSchemaRegistry();
            _queries = new ComponentQueryPreparer(_world, components.Bindings, schemas, _scope);
            Preparation = new LuaSimulationPreparation(new Registry(definitions), Lifecycle, new Application(Side.Server),
                new SystemRegistry(), new Time(), components, schemas, _queries, _scope, new ScriptSystemOptions(), Logger, scheduler);
        }

        internal ValueTask StartAsync() => Preparation.PrepareAsync(default);
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ((object)Preparation as IDisposable)?.Dispose();
            Lifecycle.Dispose();
            _queries.Dispose();
            _scope.Dispose();
            _world.Destroy();
        }
    }

    private sealed class Registry(ModDefinition[] definitions) : IModsRegistry
    {
        public JobHandle Scan(ExecutionSide side, CancellationToken cancellationToken = default) => JobHandle.Completed;
        public IEnumerable<ModDefinition> GetMods() => definitions;
    }

    private sealed class Loader(List<string> events) : IScriptLoader
    {
        internal readonly Dictionary<string, string> Sources = new(StringComparer.Ordinal);
        internal readonly List<int> InvocationThreads = [];
        internal readonly List<int> EventThreads = [];
        internal bool DelayNextLoad;
        internal IScriptRuntime? NextRuntime;
        private JobHandleCompletionSource<IScriptRuntime>? _delayed;
        private IScriptRuntime? _delayedRuntime;

        internal void CompleteDelayedLoad() => _delayed!.SetResult(_delayedRuntime!);
        public JobHandle<IScriptRuntime> Load(ModDefinition definition, ExecutionSide side)
        {
            InvocationThreads.Add(Environment.CurrentManagedThreadId);
            IScriptRuntime runtime;
            if (NextRuntime is not null)
            {
                runtime = NextRuntime;
                NextRuntime = null;
            }
            else
            {
                var script = new Script(CoreModules.Preset_SoftSandbox);
                script.Options.DebugPrint = message =>
                {
                    events.Add(message);
                    EventThreads.Add(Environment.CurrentManagedThreadId);
                };
                var entry = script.DoString(Sources[definition.MetaData.Id]).Table;
                runtime = new LuaScriptRuntime(script, [entry], definition.MetaData.Id, ["Shared/main.lua"]);
            }
            if (!DelayNextLoad) return JobHandle<IScriptRuntime>.FromResult(runtime);
            DelayNextLoad = false;
            _delayed = new JobHandleCompletionSource<IScriptRuntime>();
            _delayedRuntime = runtime;
            return _delayed.JobHandle;
        }
    }

    private sealed class TrackingRuntime : IScriptRuntime
    {
        internal int LoadCount, DisposeCount, DisposalThread;
        internal bool ThrowOnDispose;
        public void Load() => LoadCount++;
        public void Start() { }
        public void Update(double dt) { }
        public void FixedUpdate(double fixedDt) { }
        public void Destroy() { }
        public void Unload() { }
        public void Dispose()
        {
            DisposeCount++;
            DisposalThread = Environment.CurrentManagedThreadId;
            if (ThrowOnDispose) throw new ScriptRuntimeException("host-dispose-failed");
        }
    }

    private sealed class Logger : ILogger<IScriptLoader>
    {
        internal readonly List<string> Errors = [];
        internal bool ThrowOnError;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
                if (ThrowOnError) throw new IOException("Host logger failed.");
            }
        }
    }
}
