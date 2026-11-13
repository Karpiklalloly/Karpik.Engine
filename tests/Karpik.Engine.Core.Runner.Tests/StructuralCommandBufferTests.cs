using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;

public sealed class StructuralCommandBufferTests
{
    public struct Position : IEcsComponent { public float X; }
    public struct Keeper : IEcsComponent { public int Value; }
    public struct ReadonlyData : IEcsComponent
    {
        public readonly int Immutable;
        public int Mutable;
        public ReadonlyData(int immutable, int mutable) { Immutable=immutable; Mutable=mutable; }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly EcsDefaultWorld World;
        public readonly ComponentCallbackScope Scope = new();
        public readonly ComponentQueryPreparer Preparer;
        public readonly StructuralCommandBuffer Commands;
        public readonly PreparedComponentHandle Position, Temperature;
        public Fixture(StructuralCommandOptions? options = null, EcsWorldConfig? config = null)
        {
            World = new EcsDefaultWorld(config);
            var clr = new ClrComponentBindings([new GeneratedBindings()]);
            clr.Register<Position>("game:Position"); clr.Register<ReadonlyData>("game:Readonly"); clr.Register<DynamicComponentAnchor>("game:Anchor");
            var schemas = new ComponentSchemaRegistry();
            schemas.ValidateAndFreeze([new ComponentSchema("game:Temperature",1,new SchemaField("Value",SchemaScalarKind.F32)), new ComponentSchema("game:Other",1,new SchemaField("Value",SchemaScalarKind.I32))]);
            Preparer = new ComponentQueryPreparer(World,clr,schemas,Scope); Preparer.Initialize();
            var query=Preparer.Prepare(new("q",[],[],[new("game:Position",false),new("game:Temperature",false)]),"owner");
            Position=query.Components["game:Position"]; Temperature=query.Components["game:Temperature"];
            Commands=Preparer.GetCommands(options ?? new());
            Scope.Enter("owner"); Commands.Begin("owner");
        }
        public int Entity() { int id=World.NewEntity(); World.GetPool<Keeper>().Add(id); return id; }
        public void Dispose() { Commands.End(); Scope.Exit(); Preparer.Dispose(); Scope.Dispose(); World.Destroy(); }
    }

    [Fact]
    public void StructuralCommand_CreateAndSnapshotAreDeferredAndCaptured()
    {
        using var f=new Fixture();
        var snapshot=f.Position.NewSnapshot(); snapshot.WriteF32(0,7);
        var pending=f.Commands.Create(); f.Commands.Add(f.Position,pending,snapshot); snapshot.WriteF32(0,99);
        Assert.Equal(0,f.World.Count);
        f.Commands.Complete();
        Assert.Equal(1,f.World.Count);
        Assert.Equal(7,f.World.GetPool<Position>().Read(f.World.Entities[0]).X);
    }

    [Fact]
    public void StructuralCommand_LateValidationFailureChangesNoDeferredState()
    {
        using var f=new Fixture(); int entity=f.Entity();
        f.Commands.Add(f.Position,entity); f.Commands.Del(f.Temperature,entity);
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete());
        Assert.False(f.Position.Has(entity));
    }

    [Fact]
    public void StructuralCommand_RejectsEmptyCreateAndExpiredTokens()
    {
        using var f=new Fixture(); var pending=f.Commands.Create();
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete());
        f.Commands.End(); f.Commands.Begin("owner");
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Add(f.Position,pending));
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete());
        Assert.Equal(0,f.World.Count);
    }
    [Fact]
    public void StructuralCommand_CaughtSubmissionErrorPoisonsAllCommands()
    {
        using var f=new Fixture(new() { MaxCommands=2 }); int entity=f.Entity();
        f.Commands.Add(f.Position,entity);
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Add(f.Temperature, default(PendingEntity)));
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete()); Assert.False(f.Position.Has(entity));
        f.Commands.End(); f.Commands.Begin("owner"); f.Commands.Add(f.Position,entity); f.Commands.Complete();
        Assert.True(f.Position.Has(entity));
    }

    [Fact]
    public void StructuralCommand_CommandAndPayloadOverflowRejectBatch()
    {
        using var f=new Fixture(new() { MaxCommands=2, MaxPayloadBytes=3 });
        var pending=f.Commands.Create(); var snapshot=f.Position.NewSnapshot();
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Add(f.Position,pending,snapshot));
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete()); Assert.Equal(0,f.World.Count);
        f.Commands.End(); f.Commands.Begin("owner"); pending=f.Commands.Create(); f.Commands.Add(f.Position,pending);
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Create());
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete()); Assert.Equal(0,f.World.Count);
    }

    [Fact]
    public void StructuralCommand_ReadonlyForeignOwnerAndForeignSimulationAreRejected()
    {
        using var f=new Fixture(); using var other=new Fixture(); int entity=f.Entity();
        var ro=f.Preparer.Prepare(new("ro",[],[],[new("game:Position")]),"owner").Components["game:Position"];
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Add(ro,entity));
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete());
        f.Commands.End(); f.Commands.Begin("owner");
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Add(other.Position,entity));
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete());
        f.Commands.End(); f.Commands.Begin("owner");
        Assert.Throws<InvalidOperationException>(()=>f.Commands.RequireOwner("foreign"));
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete());
        Assert.False(f.Position.Has(entity));
    }

    [Fact]
    public void StructuralCommand_LastComponentIsRejectedButUndeclaredKeeperCounts()
    {
        using var f=new Fixture(); int lonely=f.World.NewEntity(); f.World.GetPool<Position>().Add(lonely);
        f.Commands.Del(f.Position,lonely);
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete()); Assert.True(f.Position.Has(lonely));
        f.Commands.End(); f.Commands.Begin("owner");
        int kept=f.Entity(); f.Position.Add(kept); f.Commands.Del(f.Position,kept); f.Commands.Complete();
        Assert.True(f.World.IsUsed(kept)); Assert.False(f.Position.Has(kept));
    }

    [Fact]
    public void StructuralCommand_DynamicLastComponentAndReplacementRespectAnchor()
    {
        using var f=new Fixture(); int entity=f.World.NewEntity(); f.Temperature.Add(entity);
        f.Commands.Del(f.Temperature,entity);
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete()); Assert.True(f.Temperature.Has(entity));
        f.Commands.End(); f.Commands.Begin("owner");
        f.Commands.Add(f.Position,entity); f.Commands.Del(f.Temperature,entity); f.Commands.Complete();
        Assert.True(f.Position.Has(entity)); Assert.False(f.Temperature.Has(entity)); Assert.Equal(1,f.World.GetComponentsCount(entity));
    }

    [Fact]
    public void StructuralCommand_CapturesGenerationAndRejectsRecycledId()
    {
        using var f=new Fixture(); int entity=f.Entity(); f.Commands.Add(f.Position,entity);
        f.World.DelEntity(entity); f.World.ReleaseDelEntityBufferAll(); int replacement=f.Entity(); Assert.Equal(entity,replacement);
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete()); Assert.False(f.Position.Has(replacement));
    }

    [Fact]
    public void StructuralCommand_UnreleasedDeletedIdsConsumeReservedEntityCapacity()
    {
        using var f=new Fixture(config:new EcsWorldConfig(entitiesCapacity:4,poolComponentsCapacity:8));
        int a=f.Entity(), b=f.Entity(), c=f.Entity(); f.World.DelEntity(a);
        Assert.Equal(2,f.World.Count); Assert.Equal(1,f.World.DelEntBufferCount);
        var pending=f.Commands.Create(); f.Commands.Add(f.Position,pending);
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete()); Assert.Equal(4,f.World.Capacity);
        Assert.False(f.Position.Has(b)); Assert.False(f.Position.Has(c));
    }

    [Fact]
    public void StructuralCommand_NativePeakOccupancyRejectsBeforeMutation()
    {
        using var f=new Fixture(config:new EcsWorldConfig(entitiesCapacity:16,poolComponentsCapacity:4));
        int a=f.Entity(), b=f.Entity(), c=f.Entity(), d=f.Entity(); f.Position.Add(a); f.Position.Add(b); f.Position.Add(c);
        f.Commands.Add(f.Position,d); f.Commands.Del(f.Position,a);
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete()); Assert.False(f.Position.Has(d)); Assert.True(f.Position.Has(a));
        f.Commands.End(); f.Commands.Begin("owner"); f.Commands.Del(f.Position,a); f.Commands.Add(f.Position,d); f.Commands.Complete();
        Assert.False(f.Position.Has(a)); Assert.True(f.Position.Has(d)); Assert.Equal(4,f.World.GetPool<Position>().Capacity);
    }

    [Fact]
    public void StructuralCommand_InterleavedPendingMaterializationAndSnapshotReuse()
    {
        using var f=new Fixture(); var snapshot=f.Position.NewSnapshot();
        var a=f.Commands.Create(); var b=f.Commands.Create(); snapshot.WriteF32(0,2); f.Commands.Add(f.Position,b,snapshot);
        snapshot.WriteF32(0,1); f.Commands.Add(f.Position,a,snapshot); f.Commands.Add(f.Temperature,b); f.Commands.Complete();
        Assert.Equal(2,f.World.Count); var pool=f.World.GetPool<Position>();
        Assert.Equal(new float[]{1,2},f.World.Entities.ToArray().Select(e=>pool.Read(e).X).Order().ToArray());
    }

    [Fact]
    public void StructuralCommand_PreexistingEmptyTargetAfterMaterializationRejectsWithoutMutation()
    {
        using var f=new Fixture(); int empty=f.World.NewEntity();
        var pending=f.Commands.Create(); f.Commands.Add(f.Position,pending); f.Commands.Add(f.Position,empty);
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete()); Assert.Equal(1,f.World.Count); Assert.False(f.Position.Has(empty));
    }

    [Fact]
    public void StructuralCommand_EmptyExistingTargetInitializedBeforeMaterializationIsStable()
    {
        using var f=new Fixture(); int empty=f.World.NewEntity(); f.Commands.Add(f.Position,empty);
        var pending=f.Commands.Create(); f.Commands.Add(f.Temperature,pending); f.Commands.Complete();
        Assert.Equal(2,f.World.Count); Assert.True(f.Position.Has(empty));
    }

    [Fact]
    public void StructuralCommand_SeededSequencesMatchStrictFifoReferenceModel()
    {
        using var f=new Fixture(new() { MaxCommands=32 }); int entity=f.Entity(); bool position=false, temperature=false;
        for(int seed=0;seed<300;seed++)
        {
            if(seed>0) { f.Commands.End(); f.Commands.Begin("owner"); }
            var random=new Random(seed); bool predictedPosition=position, predictedTemperature=temperature, valid=true;
            int count=random.Next(0,25);
            for(int i=0;i<count;i++)
            {
                bool p=random.Next(2)==0, add=random.Next(2)==0;
                bool present=p?predictedPosition:predictedTemperature;
                if(present==add) valid=false;
                if(p) predictedPosition=add; else predictedTemperature=add;
                var handle=p?f.Position:f.Temperature;
                if(add) f.Commands.Add(handle,entity); else f.Commands.Del(handle,entity);
            }
            if(valid) { f.Commands.Complete(); position=predictedPosition; temperature=predictedTemperature; }
            else Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete());
            Assert.Equal(position,f.Position.Has(entity)); Assert.Equal(temperature,f.Temperature.Has(entity));
        }
    }

    [Fact]
    public void StructuralCommand_WarmedNativeDynamicFullCyclesAllocateZero()
    {
        using var f=new Fixture(); int entity=f.Entity(); var p=f.Position.NewSnapshot(); var t=f.Temperature.NewSnapshot();
        p.WriteF32(0,3); t.WriteF32(0,4); f.Commands.End();
        for(int i=0;i<2000;i++) Cycle();
        long start=GC.GetAllocatedBytesForCurrentThread(); for(int i=0;i<1000;i++) Cycle();
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-start);
        void Cycle()
        {
            f.Commands.Begin("owner"); f.Commands.Add(f.Position,entity,p); f.Commands.Add(f.Temperature,entity,t);
            f.Commands.Del(f.Position,entity); f.Commands.Del(f.Temperature,entity); f.Commands.Complete(); f.Commands.End();
        }
    }

    [Fact]
    public void StructuralCommand_SnapshotInitializationDoesNotWriteReadonlyLeaves()
    {
        using var f=new Fixture();
        var handle=f.Preparer.Prepare(new("readonly-data",[],[],[new("game:Readonly",false)]),"owner").Components["game:Readonly"];
        int source=f.Entity(); f.World.GetPool<ReadonlyData>().Add(source)=new ReadonlyData(42,7);
        var snapshot=handle.Copy(source); var pending=f.Commands.Create(); f.Commands.Add(handle,pending,snapshot); f.Commands.Complete();
        int found=0;
        foreach(int e in f.World.Entities)
        {
            if(e==source) continue;
            Assert.Equal(0,f.World.GetPool<ReadonlyData>().Read(e).Immutable); Assert.Equal(7,f.World.GetPool<ReadonlyData>().Read(e).Mutable); found++;
        }
        Assert.Equal(1,found);
    }

    [Fact]
    public void StructuralCommand_WarmedCreationWithNativeAndDynamicPayloadAllocatesZero()
    {
        using var f=new Fixture(); var p=f.Position.NewSnapshot(); var t=f.Temperature.NewSnapshot();
        p.WriteF32(0,3); t.WriteF32(0,4); f.Commands.End();
        for(int i=0;i<1000;i++) { Cycle(); Cleanup(); }
        long bytes=0;
        for(int i=0;i<1000;i++)
        {
            long start=GC.GetAllocatedBytesForCurrentThread(); Cycle(); bytes+=GC.GetAllocatedBytesForCurrentThread()-start; Cleanup();
        }
        Assert.Equal(0,bytes);
        void Cycle()
        {
            f.Commands.Begin("owner"); var entity=f.Commands.Create(); f.Commands.Add(f.Position,entity,p); f.Commands.Add(f.Temperature,entity,t);
            f.Commands.Complete(); f.Commands.End();
        }
        void Cleanup() { int e=f.World.Entities[0]; f.World.DelEntity(e); f.World.ReleaseDelEntityBufferAll(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralCommand_ListenerFailureOrReentrantMutationIsFatal(bool mutate)
    {
        using var f=new Fixture(); int entity=f.Entity(); f.Commands.Add(f.Position,entity);
        var listener=new MigrateListener(e=>
        {
            if(mutate) f.World.NewEntity(); else throw new InvalidOperationException("listener failure");
        });
        f.World.AddListener(listener);
        try { Assert.Throws<StructuralCommandPlaybackException>(()=>f.Commands.Complete()); }
        finally { f.World.RemoveListener(listener); }
    }
    private sealed class MigrateListener(Action<int> action) : IEcsEntityEventListener
    {
        public void OnNewEntity(int entityID) { }
        public void OnMigrateEntity(int entityID) => action(entityID);
        public void OnDelEntity(int entityID) { }
    }

    [Fact]
    public void StructuralCommand_PlaybackCancellationPoisonsPartiallyAppliedBatch()
    {
        using var f=new Fixture(); int entity=f.Entity(); f.Commands.Add(f.Position,entity);
        var listener=new MigrateListener(_=>throw new OperationCanceledException()); f.World.AddListener(listener);
        try { Assert.Throws<OperationCanceledException>(()=>f.Commands.Complete()); }
        finally { f.World.RemoveListener(listener); }
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Add(f.Temperature,entity));
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete());
    }

    [Fact]
    public void StructuralCommand_TechnicalAnchorCannotBeSubmittedEvenWhenHostRegisteredIt()
    {
        using var f=new Fixture(); int entity=f.Entity();
        var anchor=f.Preparer.Prepare(new("anchor",[],[],[new("game:Anchor",false)]),"owner").Components["game:Anchor"];
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Add(anchor,entity));
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete());
        Assert.False(f.World.GetPool<DynamicComponentAnchor>().Has(entity));
        f.Commands.End(); f.Commands.Begin("owner"); f.Temperature.Add(entity);
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Del(anchor,entity));
        Assert.Throws<InvalidOperationException>(()=>f.Commands.Complete()); Assert.True(f.Temperature.Has(entity));
    }

}
