using System.Runtime.CompilerServices;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;

public sealed class LuaHotReloadBindingsTests
{
    public struct Position : IEcsComponent { public float X; }

    private sealed class Fixture : IDisposable
    {
        public readonly EcsDefaultWorld World = new();
        public readonly ComponentCallbackScope Scope = new();
        public readonly ComponentPointerRegistry Pointers = new();
        public readonly ComponentQueryPreparer Root;
        public readonly ComponentPointerType PointerType;
        public Fixture()
        {
            PointerType = Pointers.RegisterType("host:Buffer", typeof(int).MakePointerType(), Pointers.CreateDomain());
            var clr = new ClrComponentBindings([new GeneratedBindings()]);
            clr.Register<Position>("test:Position");
            var schemas = new ComponentSchemaRegistry();
            schemas.ValidateAndFreeze([new ComponentSchema("test:State", 1,
                new SchemaField("Nested", SchemaValue.Struct(new SchemaField("Value", SchemaScalarKind.I32))),
                new SchemaField("Values", SchemaValue.Fixed(SchemaValue.Scalar(SchemaScalarKind.I64), 2)),
                new SchemaField("Pointer", SchemaValue.Pointer("host:Buffer")))]);
            Root = new ComponentQueryPreparer(World, clr, schemas, Scope, Pointers);
            Root.Initialize();
        }
        public PreparedComponentQuery Query(ComponentQueryPreparer preparer) => preparer.Prepare(
            new("q", [], [], [new("test:Position", false), new("test:State", false)]), "owner");
        public int Entity() { int entity = World.NewEntity(); World.GetPool<Position>().Add(entity).X = 7; return entity; }
        public void Dispose() { Root.Dispose(); Scope.Dispose(); Pointers.Dispose(); World.Destroy(); }
    }

    [Fact]
    public void CandidateScopeRejectsAccessUntilActivation()
    {
        using var f = new Fixture();
        using var candidate = f.Root.CreateRevision();
        var query = f.Query(candidate);
        Assert.Throws<InvalidOperationException>(() => candidate.CallbackScope.Enter("owner"));
        candidate.ActivateRevision();
        candidate.CallbackScope.Enter("owner");
        query.Capture();
        candidate.CallbackScope.Exit();
    }

    [Fact]
    public void RetiringInitialRevisionPreservesNativeDynamicCompositeAndPointerData()
    {
        using var f = new Fixture(); int entity = f.Entity();
        var original = f.Query(f.Root).Components["test:State"];
        f.Scope.Enter("owner");
        var view = original.Add(entity);
        int nested = Field(original, "Nested.Value"), values = Field(original, "Values[2]"), pointer = Field(original, "Pointer");
        view.WriteI32(nested, 42); view.WriteI64(values, long.MaxValue);
        var token = f.Pointers.Capture(f.PointerType, 16); view.WritePointer(pointer, token);
        var snapshot = original.Copy(entity);
        f.Scope.Exit();
        using var candidate = f.Root.CreateRevision(); var current = f.Query(candidate).Components["test:State"];
        f.Root.RetireRevision(); candidate.ActivateRevision();
        candidate.CallbackScope.Enter("owner");
        Assert.Equal(42, current.Get(entity).ReadI32(nested));
        Assert.Equal(long.MaxValue, current.Get(entity).ReadI64(values));
        Assert.Equal(token, current.Get(entity).ReadPointer(pointer));
        Assert.Equal(7, f.World.GetPool<Position>().Read(entity).X);
        Assert.True(f.World.GetPool<DynamicComponentAnchor>().Has(entity));
        Assert.Throws<ObjectDisposedException>(() => original.Has(entity));
        Assert.Throws<ObjectDisposedException>(() => view.ReadI32(nested));
        Assert.Throws<ObjectDisposedException>(() => snapshot.ReadI32(nested));
        candidate.CallbackScope.Exit();
        // The persistent root remains a factory after its initial access domain retires.
        using var next = f.Root.CreateRevision();
    }

    [Fact]
    public void DiscardedCandidateDoesNotAffectActiveDataOrCommands()
    {
        using var f = new Fixture(); int entity = f.Entity();
        var active = f.Query(f.Root).Components["test:Position"];
        var activeCommands = f.Root.GetCommands(new());
        var candidate = f.Root.CreateRevision(); f.Query(candidate);
        var candidateCommands = candidate.GetCommands(new());
        Assert.NotSame(activeCommands, candidateCommands);
        candidate.Dispose(); candidate.Dispose();
        f.Scope.Enter("owner");
        Assert.Equal(7, active.Get(entity).ReadF32(0));
        activeCommands.Begin("owner"); activeCommands.Complete(); activeCommands.End();
        f.Scope.Exit();
        Assert.Throws<ObjectDisposedException>(() => candidate.CallbackScope.Enter("owner"));
    }

    [Fact]
    public void SimulationDisposalInvalidatesEveryLiveRevision()
    {
        using var f = new Fixture(); int entity = f.Entity();
        using var first = f.Root.CreateRevision(); var handle = f.Query(first).Components["test:Position"];
        using var second = f.Root.CreateRevision(); f.Query(second);
        first.ActivateRevision(); first.CallbackScope.Enter("owner");
        var snapshot = handle.Copy(entity); first.CallbackScope.Exit();
        f.Root.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.CallbackScope.Enter("owner"));
        Assert.Throws<ObjectDisposedException>(() => second.CallbackScope.Enter("owner"));
        Assert.Throws<ObjectDisposedException>(() => snapshot.ReadF32(0));
        Assert.Throws<ObjectDisposedException>(() => f.Root.CreateRevision());
    }

    [Fact]
    public void RetiredRevisionCannotBeReactivatedOrReuseSameOwner()
    {
        using var f = new Fixture(); int entity = f.Entity();
        using var previous = f.Root.CreateRevision(); var old = f.Query(previous).Components["test:Position"];
        previous.ActivateRevision(); previous.RetireRevision();
        using var current = f.Root.CreateRevision(); var handle = f.Query(current).Components["test:Position"];
        current.ActivateRevision(); current.CallbackScope.Enter("owner");
        Assert.Equal(7, handle.Get(entity).ReadF32(0));
        Assert.Throws<ObjectDisposedException>(() => old.Get(entity));
        Assert.Throws<ObjectDisposedException>(() => previous.ActivateRevision());
        current.CallbackScope.Exit();
    }

    [Fact]
    public void RepeatedCandidateDisposeReleasesQueryAndPreparerRoots()
    {
        using var f = new Fixture();
        var references = new List<WeakReference>();
        for (int i = 0; i < 32; i++) references.AddRange(CreateAndDiscard(f));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        using var active = f.Root.CreateRevision(); f.Query(active); active.ActivateRevision();
        active.CallbackScope.Enter("owner"); active.CallbackScope.Exit();
    }

    [Fact]
    public void WarmedRevisionAccessAndCommandsAllocateZero()
    {
        using var f = new Fixture(); int entity = f.Entity();
        using var active = f.Root.CreateRevision(); var handle = f.Query(active).Components["test:Position"];
        var commands = active.GetCommands(new()); active.ActivateRevision();
        for (int i = 0; i < 100; i++) Access();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Access();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Access()
        {
            active.CallbackScope.Enter("owner"); commands.Begin("owner");
            handle.Get(entity).WriteF32(0, 7); commands.Complete(); commands.End(); active.CallbackScope.Exit();
        }
    }

    private static int Field(PreparedComponentHandle handle, string name)
    {
        for (int i = 0; i < handle.Fields.Length; i++) if (handle.Fields.Span[i].Name == name) return i;
        throw new InvalidOperationException(name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateAndDiscard(Fixture f)
    {
        var candidate = f.Root.CreateRevision(); var query = f.Query(candidate);
        candidate.GetCommands(new()); candidate.Dispose();
        return [new WeakReference(candidate), new WeakReference(query)];
    }
}
