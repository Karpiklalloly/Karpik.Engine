using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Engine.Shared.Modding.Lua.Systems;
using DCFApixels.DragonECS;
using MoonSharp.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed partial class LuaPreparationTests
{
    private const string CatalogRunnerScript = """
        local frame = 0
        local snapshots = {}
        local buffers = {}
        local pointViews = {}
        return {
          components = { Catalog = { version = 1, fields = {
            State = { kind='struct', fields={
              Flag='bool', I8='i8', U8='u8', I16='i16', U16='u16', I32='i32', U32='u32',
              Character='char', Single='f32', Double='f64', Signed='i64', Unsigned='u64',
              Decimal='decimal', NativeSigned='nint', NativeUnsigned='nuint',
              Flags={kind='enum',underlying='u32'}
            } },
            Points = { kind='fixed', length=2, element={kind='struct',fields={X='f32',Weight='decimal'}} }
          } } },
          systems = { { id='catalog', phase='update', queries={ q={
            inc={['probe:position']={}}, opt={['probe:Catalog']={readonly=false}}
          } }, callback=function(dt,ctx)
            frame=frame+1
            local q=ctx.queries.q
            local positions=q.components['probe:position']
            local values=q.components['probe:Catalog']
            local count=0
            for e in q:entities() do
              count=count+1
              local p=positions:get(e)
              if frame==1 then
                local v=values:add(e)
                v.State.Flag=true; v.State.I8=-128; v.State.U8=255
                v.State.I16=-32768; v.State.U16=65535; v.State.I32=-2147483648; v.State.U32=4294967295
                v.State.Character=55296; v.State.Single=p.X+0.25; v.State.Double=16777217
                v.State.Signed=types.i64('9007199254740993')
                v.State.Unsigned=types.u64('18446744073709551615')
                v.State.Decimal=types.decimal('0.1')
                v.State.NativeSigned=types.i64('-23'); v.State.NativeUnsigned=types.u64('23')
                v.State.Flags=4294967295
                assert(#v.Points==2)
                v.Points[1].X=p.X+1; v.Points[2].X=p.X+2
                v.Points[1].Weight=types.decimal('0.1'); v.Points[2].Weight=types.decimal('0.3')
                snapshots[e]=values:copy(e)
                local buffer=values:new_snapshot()
                assert(buffer.State.Signed==types.i64('0') and buffer.Points[1].X==0)
                buffers[e]=buffer; pointViews[e]=buffer.Points[1]
                v.State.Signed=v.State.Signed+types.i64('1')
                v.Points[1].Weight=v.Points[1].Weight+types.decimal('0.1')
                values:copy_to(e,buffer)
                assert(buffer.Points[1]==pointViews[e])
              else
                assert(frame==2)
                local v=values:get(e)
                local old=snapshots[e]
                local buffer=buffers[e]
                assert(old.State.Flag and old.State.I8==-128 and old.State.U8==255)
                assert(old.State.I16==-32768 and old.State.U16==65535)
                assert(old.State.I32==-2147483648 and old.State.U32==4294967295)
                assert(old.State.Character==55296 and old.State.Single==p.X+0.25 and old.State.Double==16777217)
                assert(old.State.Signed==types.i64('9007199254740993'))
                assert(old.State.Unsigned==types.u64('18446744073709551615'))
                assert(old.State.Decimal==types.decimal('0.1'))
                assert(old.State.NativeSigned==types.i64('-23') and old.State.NativeUnsigned==types.u64('23'))
                assert(old.State.Flags==4294967295)
                assert(old.Points[1].X==p.X+1 and old.Points[2].X==p.X+2)
                assert(old.Points[1].Weight==types.decimal('0.1') and old.Points[2].Weight==types.decimal('0.3'))
                assert(v.State.Signed==types.i64('9007199254740994') and v.Points[1].Weight==types.decimal('0.2'))
                assert(buffer.State.Signed==types.i64('9007199254740994'))
                assert(buffer.Points[1].Weight==types.decimal('0.2'))
                old.State.I32=17; buffer.State.I32=99
                assert(v.State.I32==-2147483648)
                local state=buffer.State
                values:copy_to(e,buffer)
                assert(buffer.State==state and buffer.Points[1]==pointViews[e])
                assert(buffer.State.I32==-2147483648 and old.State.I32==17)
                v.State.Single=v.State.Single+1
              end
            end
            assert(count==2)
            print('catalog:'..frame)
          end } }
        }
        """;

    [Theory]
    [InlineData(false, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(false, EcsUpdateSchedulerMode.Parallel)]
    [InlineData(true, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(true, EcsUpdateSchedulerMode.Parallel)]
    public async Task LuaCatalog_RealRunnerPreservesExactNestedSnapshotsAcrossFrames(
        bool useStatic, EcsUpdateSchedulerMode mode)
    {
        await WithLuaFiles(CatalogRunnerScript, null, (_, definition) =>
        {
            var logger = new ScriptLogger(); var installer = new LuaProbeInstaller(definition, logger);
            var runner = new EngineRunner(NullLoggerFactory.Instance) { UpdateSchedulerMode = mode };
            if (useStatic) runner.RegisterStaticComposition(new LuaProbeComposition(installer, logger));
            else
            {
                runner.RegisterModule(installer);
                runner.RegisterModule(new Karpik.Engine.Shared.Modding.Lua.ModdingLuaModuleInstaller());
            }
            ComponentSnapshot? retained = null;
            int signedIndex = -1;
            try
            {
                var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
                runner.Setup(new Application(Side.Server), scheduler); scheduler.Execute();
                var bindings = installer.Bindings!;
                var positions = bindings.World.GetPool<LuaPosition>();
                int first = bindings.World.NewEntity(); positions.Add(first).X = 10;
                int second = bindings.World.NewEntity(); positions.Add(second).X = 20;
                runner.RunGameplayFrame(0.02);
                runner.RunGameplayFrame(0.02);
                Assert.Equal(new[] { "[Mod: probe] catalog:1", "[Mod: probe] catalog:2" },
                    logger.Messages.Where(message => message.StartsWith("[Mod: probe] catalog:", StringComparison.Ordinal)));
                var query = bindings.Queries.Prepare(new("verify", [], [], [new("probe:Catalog", false)]), "test:catalog");
                var handle = query.Components["probe:Catalog"];
                var fields = handle.Fields.ToArray();
                signedIndex = Field("State.Signed");
                bindings.CallbackScope.Enter("test:catalog");
                try
                {
                    foreach (int entity in new[] { first, second })
                    {
                        var view = handle.Get(entity);
                        Assert.Equal(9007199254740994L, view.ReadI64(signedIndex));
                        Assert.Equal(ulong.MaxValue, view.ReadU64(Field("State.Unsigned")));
                        Assert.Equal(0.1m, view.ReadDecimal(Field("State.Decimal")));
                        Assert.Equal(0.2m, view.ReadDecimal(Field("Points[1].Weight")));
                        Assert.Equal(int.MinValue, view.ReadI32(Field("State.I32")));
                        Assert.Equal(positions.Get(entity).X + 1.25f, view.ReadF32(Field("State.Single")));
                    }
                    retained = handle.Copy(first);
                }
                finally { bindings.CallbackScope.Exit(); }
                Assert.Equal(9007199254740994L, retained.ReadI64(signedIndex));
                Assert.Equal(10, positions.Get(first).X); Assert.Equal(20, positions.Get(second).X);
                int Field(string name)
                {
                    int index = Array.FindIndex(fields, field => field.Name == name);
                    Assert.True(index >= 0, "Missing prepared field: " + name);
                    return index;
                }
            }
            finally { runner.Destroy(); }
            Assert.NotNull(retained);
            Assert.Throws<ObjectDisposedException>(() => retained.ReadI64(signedIndex));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(ScriptSystemErrorPolicy.StopSimulation, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(ScriptSystemErrorPolicy.StopSimulation, EcsUpdateSchedulerMode.Parallel)]
    [InlineData(ScriptSystemErrorPolicy.LogAndContinue, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(ScriptSystemErrorPolicy.LogAndContinue, EcsUpdateSchedulerMode.Parallel)]
    public void LuaCatalog_ScheduledNestedExactFailurePreservesWritesAndHonorsPolicy(
        ScriptSystemErrorPolicy policy, EcsUpdateSchedulerMode mode)
    {
        var script = new Script(); LuaExactValues.Register(script);
        var runtime = new LuaScriptRuntime(script, [script.DoString("""
            return {components={Values={version=1,fields={State={kind='struct',fields={Signed='i64',Byte='u8'}}}}},systems={
              {id='fail',phase='update',queries={q={inc={['catalog:Values']={readonly=false}}}},callback=function(dt,ctx)
                local q=ctx.queries.q
                for e in q:entities() do
                  local value=q.components['catalog:Values']:get(e)
                  value.State.Signed=types.i64('9007199254740993')
                  value.State.Byte=0.5
                end
              end},
              {id='later',phase='update',after={'catalog:fail'},callback=function(dt,ctx) later=(later or 0)+1 end}
            }}
            """).Table], "catalog", ["main.lua"]);
        var world = new EcsDefaultWorld(); var scope = new ComponentCallbackScope();
        var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze(runtime.Components);
        var preparer = new ComponentQueryPreparer(world, new ClrComponentBindings([]), schemas, scope);
        var owner = new ModContainer(new ModDefinition(new ModMetaData("catalog", "Catalog", "test", "1", ""), "."), runtime);
        using var scheduler = new EcsUpdateScheduler();
        try
        {
            preparer.Initialize(); runtime.PrepareEcs(preparer, scope, new ScriptSystemOptions { ErrorPolicy = policy }, NullLogger.Instance);
            owner.Load(); owner.Start();
            var time = new Time(); time.Update(0.02);
            var systems = new ISystemUpdate[runtime.Systems.Length];
            var descriptors = new EcsUpdateSystemDescriptor[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i] = new LuaUpdateSystem(owner, runtime, i, time);
                var declaration = runtime.Systems[i];
                descriptors[i] = new(typeof(LuaUpdateSystem), true, runtime.GetContext(i).Accesses, declaration.Orders, declaration.Id);
            }
            scheduler.InitializeInstances(systems, descriptors, mode);
            var inspect = preparer.Prepare(new("inspect", [], [], [new("catalog:Values", false)]), "test:policy");
            var handle = inspect.Components["catalog:Values"];
            int entity = world.NewEntity();
            scope.Enter("test:policy"); try { handle.Add(entity); } finally { scope.Exit(); }
            for (int frame = 1; frame <= 2; frame++)
            {
                if (policy == ScriptSystemErrorPolicy.StopSimulation)
                {
                    var error = Assert.Throws<ScriptRuntimeException>(scheduler.Update);
                    Assert.Contains("catalog:fail", error.Message); Assert.Contains("State.Byte", error.Message);
                    Assert.True(script.Globals.Get("later").IsNil());
                }
                else
                {
                    scheduler.Update(); Assert.Equal((double)frame, script.Globals.Get("later").Number);
                }
                scope.Enter("test:policy");
                try
                {
                    var view = handle.Get(entity); var fields = handle.Fields.ToArray();
                    Assert.Equal(9007199254740993L, view.ReadI64(Array.FindIndex(fields, f => f.Name == "State.Signed")));
                    Assert.Equal((byte)0, view.ReadU8(Array.FindIndex(fields, f => f.Name == "State.Byte")));
                }
                finally { scope.Exit(); }
            }
        }
        finally { owner.Dispose(); preparer.Dispose(); scope.Dispose(); world.Destroy(); }
    }

}
