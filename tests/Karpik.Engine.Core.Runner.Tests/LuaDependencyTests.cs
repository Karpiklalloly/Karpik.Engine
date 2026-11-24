using System.Collections.Frozen;
using Autofac;
using DCFApixels.DragonECS;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Engine.Shared.Modding.Lua.Systems;
using Microsoft.Extensions.Logging.Abstractions;
using MoonSharp.Interpreter;
using Xunit;

public sealed partial class LuaPreparationTests
{
    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, false)]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, true)]
    public void ModDependencyLua_ResolvedLifecycleOrderSurvivesCodeReloadAfterManifestEdit(Side side, bool useStatic)
    {
        using var host = new DependencyLuaHost();
        host.StartOrderedPair(side, useStatic);
        var world = host.Bindings.World;
        int entity = world.NewEntity();
        var identity = world.GetEntityLong(entity);
        world.GetPool<LuaPosition>().Add(entity).X = 42;
        // Re-resolving these manifests would reverse the order, not just drop the edge.
        host.Manifest("a-consumer", "{}");
        host.Manifest("z-provider", """{"shared":[{"id":"a-consumer"}]}""");
        host.WriteLua("a-consumer", "Shared", DependencyLifecycleScript("new"));
        host.WriteLua("z-provider", "Shared", DependencyLifecycleScript("new"));
        host.Logger.Messages.Clear();
        var result = host.Reload();
        Assert.True(result.Status == SimulationCodeReloadStatus.Success, result.ToString());
        Assert.Equal(["z-provider", "a-consumer"], host.Lifecycle.Containers.Select(mod => mod.ModDefinition.MetaData.Id));
        Assert.Equal([
            "[Mod: z-provider] top:new", "[Mod: z-provider] load:new",
            "[Mod: a-consumer] top:new", "[Mod: a-consumer] load:new",
            "[Mod: z-provider] reload:new", "[Mod: a-consumer] reload:new",
            "[Mod: a-consumer] destroy:old", "[Mod: a-consumer] unload:old",
            "[Mod: z-provider] destroy:old", "[Mod: z-provider] unload:old"
        ], host.Logger.Messages);
        Assert.Same(world, host.Bindings.World);
        Assert.Equal(identity, world.GetEntityLong(entity));
        Assert.Equal(42f, world.GetPool<LuaPosition>().Read(entity).X);
    }

    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, false)]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, true)]
    public void ModDependencyLua_StartsProviderBeforeConsumer(Side side, bool useStatic)
    {
        using var host = new DependencyLuaHost();
        host.StartOrderedPair(side, useStatic);
        Assert.Equal([
            "[Mod: z-provider] top:old", "[Mod: z-provider] load:old",
            "[Mod: a-consumer] top:old", "[Mod: a-consumer] load:old",
            "[Mod: z-provider] start:old", "[Mod: a-consumer] start:old"
        ], host.Logger.Messages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModDependencyLua_UsesOrdinalLifecycleWithoutResolvedEdges(bool useCustomRegistry)
    {
        using var host = new DependencyLuaHost();
        // Deliberately supply the custom registry in non-ordinal order.
        if (useCustomRegistry)
        {
            var custom = new Registry([host.Definition("z-mod"), host.Definition("a-mod")]);
            custom.Scan(ExecutionSide.Server).GetAwaiter().GetResult();
            host.CustomRegistry = custom;
        }
        host.Manifest("z-mod", "{}");
        host.Manifest("a-mod", "{}");
        host.WriteLua("z-mod", "Shared", "return { on_load = function() print('load') end }");
        host.WriteLua("a-mod", "Shared", "return { on_load = function() print('load') end }");
        host.Setup(Side.Server, useStatic: false);
        Assert.Equal(["[Mod: a-mod] load", "[Mod: z-mod] load"], host.Logger.Messages);
    }

    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, true)]
    public void ModDependencyLua_QueriesOnlyOwnResolvedDirectIdsFromSharedAndSide(Side side, bool useStatic)
    {
        using var host = new DependencyLuaHost();
        host.DeclareQueryGraph(side);
        host.WriteQueryEntries(side, "initial");
        host.Setup(side, useStatic);
        Assert.Equal(["[Mod: a-consumer] load:Shared:initial", $"[Mod: a-consumer] load:{side}:initial"],
            host.Logger.Messages.Where(message => message.StartsWith("[Mod: a-consumer] ", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, false)]
    public void ModDependencyLua_UsesFrozenDirectIdsInReloadCandidate(Side side, bool useStatic)
    {
        using var host = new DependencyLuaHost();
        host.DeclareQueryGraph(side);
        host.WriteLua("a-consumer", "Shared", "return {}");
        host.Setup(side, useStatic);
        var old = host.Lifecycle.Containers.Single(mod => mod.ModDefinition.MetaData.Id == "a-consumer");
        host.Manifest("a-consumer", """{"shared":[{"id":"unrelated"}]}""");
        host.WriteQueryEntries(side, "reloaded");
        var result = host.Reload();
        Assert.True(result.Status == SimulationCodeReloadStatus.Success, result.ToString());
        Assert.NotSame(old, host.Lifecycle.Containers.Single(mod => mod.ModDefinition.MetaData.Id == "a-consumer"));
        Assert.Equal(["[Mod: a-consumer] load:Shared:reloaded", $"[Mod: a-consumer] load:{side}:reloaded"],
            host.Logger.Messages.Where(message => message.StartsWith("[Mod: a-consumer] ", StringComparison.Ordinal)));
    }

    [Fact]
    public Task ModDependencyLua_UnresolvedDefinitionHasAnEmptyDependencySet() =>
        WithLuaFiles("assert(has_dependency('provider') == false); return {}", null, async (loader, definition) =>
        {
            using var runtime = await loader.Load(definition, ExecutionSide.Server);
        });

    [Theory]
    [InlineData("")]
    [InlineData("nil")]
    [InlineData("123")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("function() end")]
    [InlineData("'provider', 'another'")]
    [InlineData("'provider', nil")]
    public Task ModDependencyLua_RejectsWrongArgumentTypeOrArity(string args) =>
        WithLuaFiles($"has_dependency({args}); return {{}}", null, async (loader, definition) =>
        {
            var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () =>
            {
                using var runtime = await loader.Load(definition, ExecutionSide.Server);
            });
            Assert.Contains("has_dependency", error.Message);
            Assert.Contains("one string", error.Message);
        });

    [Fact]
    public void ModDependencyLua_WarmedBridgeHitAndMissDoNotAllocate()
    {
        var script = new Script(CoreModules.None);
        LuaScriptLoader.RegisterDependencyQuery(script, new[] { "z-provider" }.ToFrozenSet(StringComparer.Ordinal));
        // Invoke the registered CLR callback directly with prebuilt arguments, excluding VM dispatch costs.
        var global = script.Globals.Get("has_dependency");
        Assert.Equal(DataType.ClrFunction, global.Type);
        var callback = global.Callback.ClrCallback;
        var hit = new CallbackArguments([DynValue.NewString("z-provider")], isMethodCall: false);
        var miss = new CallbackArguments([DynValue.NewString("Z-provider")], isMethodCall: false);
        const int iterations = 10_000;
        for (int i = 0; i < iterations; i++)
        {
            callback(null!, hit);
            callback(null!, miss);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            callback(null!, hit);
            callback(null!, miss);
        }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Same(DynValue.True, callback(null!, hit));
        Assert.Same(DynValue.False, callback(null!, miss));
    }

    private static string DependencyQueryScript(string entry, string revision, string active, string inactive) => $$"""
        local function check()
            assert(type(has_dependency) == 'function', 'dependency query not installed')
            assert(has_dependency('z-shared') == true)
            assert(has_dependency('{{active}}') == true)
            for _, id in ipairs({'{{inactive}}', 'transitive', 'unrelated', 'missing', 'wrong-side', 'Z-shared', '', 'a-consumer'}) do
                assert(has_dependency(id) == false, id)
            end
        end
        check()
        return { on_load = function() check(); print('load:{{entry}}:{{revision}}') end }
        """;

    private static string DependencyLifecycleScript(string revision) => $$"""
        print('top:{{revision}}')
        return {
            on_load = function() print('load:{{revision}}') end,
            on_start = function() print('start:{{revision}}') end,
            on_reload = function() print('reload:{{revision}}') end,
            on_destroy = function() print('destroy:{{revision}}') end,
            on_unload = function() print('unload:{{revision}}') end
        }
        """;

    [Module(ModuleScope.Simulation)]
    private sealed class DependencyLuaHost : IModuleInstaller, IStaticRuntimeComposition, IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("karpik-lua-dependency-").FullName;
        private readonly EngineRunner _runner = new(NullLoggerFactory.Instance);
        private readonly MainThreadScheduler _scheduler = new(Environment.CurrentManagedThreadId, NullLoggerFactory.Instance);
        internal readonly ScriptLogger Logger = new();
        internal LuaBindingsTestScope Bindings { get; private set; } = null!;
        internal ModsLifecycleManager Lifecycle { get; private set; } = null!;
        internal IModsRegistry? CustomRegistry { get; set; }

        public string Name => nameof(DependencyLuaHost);
        private string ModPath(string id) => Path.Combine(_root, "Mods", id);
        internal ModDefinition Definition(string id) => new(new ModMetaData(id, id, "test", "1.0.0", ""), ModPath(id));
        internal void Manifest(string id, string dependencies) => File.WriteAllText(
            Path.Combine(Directory.CreateDirectory(ModPath(id)).FullName, "mod_info.json"),
            $$"""{"id":"{{id}}","version":"1.0.0","dependencies":{{dependencies}}}""");
        internal void WriteLua(string id, string side, string source) => File.WriteAllText(
            Path.Combine(Directory.CreateDirectory(Path.Combine(ModPath(id), side)).FullName, "main.lua"), source);

        internal void Setup(Side side, bool useStatic)
        {
            _runner.EnableLocalCodeReload();
            if (useStatic) _runner.RegisterStaticComposition(this);
            else
            {
                _runner.RegisterModule(this);
                _runner.RegisterTypes([typeof(ModsRegistry), typeof(LuaModSetPreparation)]);
            }
            string previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = _root;
                _scheduler.RunUntilCompleted(_runner.SetupAsync(new Application(side), _scheduler));
            }
            finally { Environment.CurrentDirectory = previous; }
        }

        internal void DeclareQueryGraph(Side side)
        {
            Manifest("a-consumer", """
                {"shared":[{"id":"z-shared"},{"id":"missing","optional":true},{"id":"wrong-side","optional":true}],
                 "client":[{"id":"client-dep"}],"server":[{"id":"server-dep"}]}
                """);
            Manifest("z-shared", """{"shared":[{"id":"transitive"}]}""");
            WriteLua("z-shared", "Shared", "return {}");
            foreach (string id in new[] { "client-dep", "server-dep", "transitive", "unrelated" })
            {
                Manifest(id, "{}");
                WriteLua(id, "Shared", "return {}");
            }
            Manifest("wrong-side", "{}");
            WriteLua("wrong-side", side == Side.Client ? "Server" : "Client", "error('opposite side was loaded')");
        }

        internal void WriteQueryEntries(Side side, string revision)
        {
            string active = side == Side.Client ? "client-dep" : "server-dep";
            string inactive = side == Side.Client ? "server-dep" : "client-dep";
            foreach (string entry in new[] { "Shared", side.ToString() })
                WriteLua("a-consumer", entry, DependencyQueryScript(entry, revision, active, inactive));
        }

        internal void StartOrderedPair(Side side, bool useStatic)
        {
            Manifest("a-consumer", """{"shared":[{"id":"z-provider"}]}""");
            Manifest("z-provider", "{}");
            WriteLua("a-consumer", "Shared", DependencyLifecycleScript("old"));
            WriteLua("z-provider", "Shared", DependencyLifecycleScript("old"));
            Setup(side, useStatic);
            new BeginSystem(Lifecycle).Begin();
        }

        internal SimulationCodeReloadResult Reload()
        {
            var request = _runner.RequestCodeReloadAsync();
            _scheduler.RunUntilCompleted(request);
            return request.GetAwaiter().GetResult();
        }

        public void OnRegisterServices(ContainerBuilder builder)
        {
            builder.Register(_ => Lifecycle = new ModsLifecycleManager(new LuaScriptLoader(new PhysicalFileSystem(), Logger)))
                .As<IModsLifecycleManager>().SingleInstance();
            var components = CreateLuaComponents();
            components.Register<LuaPosition>("probe:position");
            builder.RegisterInstance(Bindings = new LuaBindingsTestScope(components));
            builder.Register(context => context.Resolve<LuaBindingsTestScope>().Create(
                CustomRegistry ?? context.Resolve<IModsRegistry>(), context.Resolve<IModsLifecycleManager>(),
                context.Resolve<Application>(), context.Resolve<ISystemRuntimeRegistry>(), context.Resolve<Time>(), Logger))
                .As<ISimulationPreparation>().SingleInstance();
        }

        public void RegisterModules(IStaticModuleRegistry registry) => registry.Add(this);
        public void RegisterServices(IStaticServiceRegistry registry)
        {
            registry.Register<IModsRegistry, ModsRegistry>(ModuleScope.ModSet, ServiceLifetime.Singleton, _ => new ModsRegistry());
            registry.Register<IModContentParticipant, LuaModSetPreparation>(ModuleScope.ModSet, ServiceLifetime.Singleton,
                services => new LuaModSetPreparation(services.Resolve<IModsRegistry>(), services.Resolve<Application>()));
            registry.Register<IModSetPreparation, LuaModSetPreparation>(ModuleScope.ModSet, ServiceLifetime.Singleton,
                services => new LuaModSetPreparation(services.Resolve<IModsRegistry>(), services.Resolve<Application>()));
        }
        public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry) { }

        public void Dispose()
        {
            try { _runner.Destroy(); }
            finally { _scheduler.Dispose(); Directory.Delete(_root, recursive: true); }
        }
    }
}
