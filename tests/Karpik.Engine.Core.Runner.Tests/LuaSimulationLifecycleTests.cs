using System.Runtime.CompilerServices;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed partial class LuaPreparationTests
{
    [Theory]
    [InlineData(false, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(false, EcsUpdateSchedulerMode.Parallel)]
    [InlineData(true, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(true, EcsUpdateSchedulerMode.Parallel)]
    public async Task DeclaredLuaSystems_RecreateScopes_DisposeOldAccess_AndReleaseReferences(
        bool useStatic, EcsUpdateSchedulerMode mode)
    {
        WeakReference[] references = [];
        await WithLuaFiles("""
            local callbacks = 0
            return {
              components = { Temperature = { version = 1, fields = { Value = 'f32', ZBool = 'bool', ZDouble = 'f64', ZInt = 'i32', ZUInt = 'u32' } } },
              on_load = function() print('load') end,
              on_start = function() print('start:' .. callbacks) end,
              on_destroy = function() print('destroy:' .. callbacks) end,
              on_unload = function() print('unload:' .. callbacks) end,
              systems = { { id = 'temperature', phase = 'update',
                queries = { pending = {
                  inc = { ['probe:position'] = {} },
                  exc = { ['probe:Temperature'] = { readonly = false } }
                } },
                callback = function(dt, ctx)
                  callbacks = callbacks + 1
                  local q = ctx.queries.pending
                  for e in q:entities() do
                    local p = q.components['probe:position']:get(e)
                    local t = q.components['probe:Temperature']:add(e)
                    t.Value = 20-p.X*0.01
                    t.ZBool = true; t.ZDouble = 16777217; t.ZInt = -2147483648; t.ZUInt = 4294967295
                  end
                end
              } }
            }
            """, null, (_, definition) =>
        {
            references = RunRepeatedLuaScopes(definition, useStatic, mode);
            return Task.CompletedTask;
        });

        // Collect only after the no-inline frame and the fixture's JobSystem have released their roots.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.Equal(51, references.Length);
        Assert.All(references, reference => Assert.False(reference.IsAlive, "A disposed Lua scope is still rooted."));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] RunRepeatedLuaScopes(ModDefinition definition, bool useStatic, EcsUpdateSchedulerMode mode)
    {
        var runner = new EngineRunner(NullLoggerFactory.Instance) { UpdateSchedulerMode = mode };
        var retired = new List<RetiredLuaScope>();
        var references = new List<WeakReference>();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var logger = new ScriptLogger();
            var installer = new LuaProbeInstaller(definition, logger);
            if (useStatic) runner.RegisterStaticComposition(new LuaProbeComposition(installer, logger));
            else
            {
                runner.RegisterModule(installer);
                runner.RegisterModule(new ModdingLuaModuleInstaller());
            }
            RetiredLuaScope current;
            try
            {
                var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
                runner.Setup(new Application(Side.Server), scheduler);
                scheduler.Execute();
                var bindings = installer.Bindings!;
                var owner = Assert.Single(installer.Lifecycle!.Containers);
                var runtime = Assert.IsType<LuaScriptRuntime>(owner.Runtime);
                var adapter = Assert.Single(installer.RuntimeRegistry!.Systems);
                var scope = bindings.CallbackScope;
                var queries = bindings.Queries;
                var query = queries.Prepare(new("retained", [], [], [new("probe:Temperature", false)]), "test:retained");
                var temperatures = query.Components["probe:Temperature"];
                foreach (var old in retired)
                {
                    Assert.NotSame(old.Bindings.World, bindings.World);
                    Assert.NotSame(old.Runtime, runtime);
                    Assert.NotSame(old.Adapter, adapter);
                    Assert.NotSame(old.Bindings.CallbackScope, scope);
                    Assert.NotSame(old.Bindings.Queries, queries);
                    Assert.NotSame(old.Query, query);
                }
                var positions = bindings.World.GetPool<LuaPosition>();
                int first = bindings.World.NewEntity(); positions.Add(first).X = 0;
                int second = bindings.World.NewEntity(); positions.Add(second).X = 100;
                runner.RunGameplayFrame(0.02);
                scope.Enter("test:retained");
                PreparedComponentView savedView;
                try
                {
                    Assert.Equal(20, temperatures.Get(first).ReadF32(0));
                    Assert.Equal(19, temperatures.Get(second).ReadF32(0));
                    savedView = temperatures.Get(first);
                    Assert.True(savedView.ReadBool(1)); Assert.Equal(16777217d, savedView.ReadF64(2));
                    Assert.Equal(int.MinValue, savedView.ReadI32(3)); Assert.Equal(uint.MaxValue, savedView.ReadU32(4));
                    savedView.WriteF32(0, 99);
                    savedView.WriteBool(1, false); savedView.WriteF64(2, 99); savedView.WriteI32(3, 99); savedView.WriteU32(4, 99);
                }
                finally { scope.Exit(); }
                owner.IsEnabled = false;
                runner.RunGameplayFrame(0.02);
                owner.IsEnabled = true;
                runner.RunGameplayFrame(0.02);
                scope.Enter("test:retained");
                try
                {
                    var view = temperatures.Get(first);
                    Assert.Equal(99, view.ReadF32(0)); Assert.False(view.ReadBool(1)); Assert.Equal(99d, view.ReadF64(2));
                    Assert.Equal(99, view.ReadI32(3)); Assert.Equal(99u, view.ReadU32(4));
                }
                finally { scope.Exit(); }
                current = new(bindings, owner, runtime, adapter, query, temperatures, savedView, first, logger);
                foreach (var value in new object[] { runtime, adapter, bindings.World, scope, queries, query, savedView })
                    references.Add(new WeakReference(value));
                foreach (var field in temperatures.Fields.ToArray())
                {
                    references.Add(new WeakReference(field));
                    object column = field.Kind switch
                    {
                        SchemaScalarKind.Bool => field.Bool, SchemaScalarKind.I32 => field.I32,
                        SchemaScalarKind.U32 => field.U32, SchemaScalarKind.F32 => field.F32,
                        SchemaScalarKind.F64 => field.F64, _ => throw new InvalidOperationException()
                    };
                    references.Add(new WeakReference(column));
                }
            }
            finally { runner.Destroy(); }

            AssertDisposedLuaScope(current);
            Assert.Equal(["[Mod: probe] load", "[Mod: probe] start:0", "[Mod: probe] destroy:2", "[Mod: probe] unload:2"],
                logger.Messages.Where(message => message.StartsWith("[Mod: probe] ", StringComparison.Ordinal)));
            current = current with { FinalLogCount = logger.Messages.Count };
            retired.Add(current);
            foreach (var old in retired)
            {
                AssertDisposedLuaScope(old);
                Assert.Equal(old.FinalLogCount, old.Logger.Messages.Count);
            }
        }
        return references.ToArray();
    }

    private static void AssertDisposedLuaScope(RetiredLuaScope old)
    {
        Assert.True(old.Bindings.World.IsDestroyed);
        Action[] calls = [old.Adapter.Update, old.Runtime.Load, old.Runtime.Start,
            () => old.Runtime.Update(0.02), () => old.Runtime.FixedUpdate(0.02), old.Runtime.Destroy, old.Runtime.Unload,
            () => old.Runtime.InvokeSystem(0, 0.02), () => old.Owner.Update(0.02), () => old.Owner.TryStartUpdate(),
            old.Bindings.Queries.Initialize, () => old.Bindings.CallbackScope.Enter("test:retained"), old.Query.Capture,
            () => old.Temperatures.Has(old.Entity), () => old.View.ReadF32(0),
            () => old.View.ReadBool(1), () => old.View.ReadF64(2), () => old.View.ReadI32(3), () => old.View.ReadU32(4),
            () => old.View.WriteBool(1, true), () => old.View.WriteF64(2, 0), () => old.View.WriteI32(3, 0), () => old.View.WriteU32(4, 0),
            () => old.View.GetFieldKind(0)];
        foreach (var call in calls) Assert.Throws<ObjectDisposedException>(call);
    }

    private sealed record RetiredLuaScope(LuaBindingsTestScope Bindings, ModContainer Owner, LuaScriptRuntime Runtime,
        ISystemUpdate Adapter, PreparedComponentQuery Query, PreparedComponentHandle Temperatures,
        PreparedComponentView View, int Entity, ScriptLogger Logger, int FinalLogCount = 0);
}
