using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using System.Runtime.InteropServices;
using Xunit;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;

public sealed unsafe class NativeCompositeBindingsTests
{
    public struct Inner { public long Count; public readonly int Locked; }
    public struct Nested { public float X; public Inner Inner; }
    public struct NativeShape : IEcsComponent
    {
        public Nested Data;
        public readonly Nested Locked;
        public fixed float Values[3];
    }
    public struct PointerShape : IEcsComponent
    {
        public int* Value;
        public void* Untyped;
        public readonly int* Locked;
    }

    [Fact]
    public void LegacyPreparerWithoutPolicyRejectsNativePointerQueriesWithContext()
    {
        var bindings = new ClrComponentBindings([new GeneratedBindings()]); bindings.Register<PointerShape>("test:Pointer");
        var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze([]);
        var world = new EcsDefaultWorld(); using var scope = new ComponentCallbackScope();
        using var preparer = new ComponentQueryPreparer(world, bindings, schemas, scope);
        try
        {
            preparer.Initialize();
            var error = Assert.Throws<InvalidDataException>(() => preparer.Prepare(
                new("pointer", [], [], [new("test:Pointer")]), "test:owner"));
            Assert.Contains("test:owner", error.Message);
            Assert.Contains("query 'pointer'", error.Message);
            Assert.Contains("test:Pointer", error.Message);
        }
        finally { preparer.Dispose(); world.Destroy(); }
    }

    [Fact]
    public void GeneratedNestedAndFixedFieldsUseDirectAccessAndPropagateReadonly()
    {
        var bindings = new ClrComponentBindings([new GeneratedBindings()]); bindings.Register<NativeShape>("test:Native");
        var world = new EcsDefaultWorld();
        try
        {
            var pool = bindings.ResolveBinding("test:Native").Bind(world); int entity = world.NewEntity(); pool.Add(entity);
            var fields = pool.Fields.ToArray().ToDictionary(field => field.Name, StringComparer.Ordinal);
            fields["Data.X"].F32.Write(entity, 12.5f); fields["Data.Inner.Count"].I64.Write(entity, long.MaxValue);
            fields["Values[1]"].F32.Write(entity, 1); fields["Values[3]"].F32.Write(entity, 3);
            ref var value = ref world.GetPool<NativeShape>().Get(entity);
            Assert.Equal(12.5f, value.Data.X); Assert.Equal(long.MaxValue, value.Data.Inner.Count);
            Assert.Equal(1, value.Values[0]); Assert.Equal(0, value.Values[1]); Assert.Equal(3, value.Values[2]);
            Assert.Equal("Data", fields["Data.Inner.Count"].Path.Span[0].Name);
            Assert.Equal("Count", fields["Data.Inner.Count"].Path.Span[2].Name);
            Assert.Equal(3, fields["Values[3]"].Path.Span[1].Index);
            Assert.False(fields["Locked.X"].CanWrite); Assert.False(fields["Data.Inner.Locked"].CanWrite);
            Assert.Throws<InvalidOperationException>(() => fields["Locked.X"].F32.Write(entity, 99));
            Assert.Throws<InvalidOperationException>(() => fields["Data.Inner.Locked"].I32.Write(entity, 99));
            Assert.Equal(0, fields["Locked.Inner.Count"].I64.Read(entity));
            var fixedField = fields["Values[2]"]; var nestedField = fields["Data.Inner.Count"];
            for (int i = 0; i < 2000; i++) { fixedField.F32.Write(entity, i); _ = fixedField.F32.Read(entity); _ = nestedField.I64.Read(entity); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { fixedField.F32.Write(entity, i); _ = fixedField.F32.Read(entity); _ = nestedField.I64.Read(entity); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(999, world.GetPool<NativeShape>().Get(entity).Values[1]);
        }
        finally { world.Destroy(); }
    }

    [Fact]
    public void GeneratedNativePointersRequireHostTypesAndRejectRevokedOrIncompatibleTokens()
    {
        var bindings = new ClrComponentBindings([new GeneratedBindings()]); bindings.Register<PointerShape>("test:Pointer");
        var binding = bindings.ResolveBinding("test:Pointer");
        var world = new EcsDefaultWorld(); using var pointers = new ComponentPointerRegistry();
        var domain = pointers.CreateDomain();
        var typed = pointers.RegisterType("host:Int", typeof(int*), domain);
        var untyped = pointers.RegisterType("host:Void", typeof(void*), domain);
        pointers.Freeze(); void* memory = NativeMemory.Alloc(16);
        try
        {
            Assert.Throws<InvalidOperationException>(() => binding.Bind(world));
            var pool = binding.Bind(world, pointers); int entity = world.NewEntity(); pool.Add(entity);
            var fields = pool.Fields.ToArray().ToDictionary(field => field.Name, StringComparer.Ordinal);
            ComponentPointerValue token = pointers.Capture(typed, (nuint)memory);
            fields["Value"].Pointer.Write(entity, token);
            Assert.Equal((nuint)memory, (nuint)world.GetPool<PointerShape>().Get(entity).Value);
            Assert.Equal((nuint)memory, pointers.ReadAddress(fields["Value"].Pointer.Read(entity), typed));
            Assert.Throws<InvalidOperationException>(() => fields["Untyped"].Pointer.Write(entity, token));
            Assert.Throws<InvalidOperationException>(() => fields["Locked"].Pointer.Write(entity, token));
            fields["Untyped"].Pointer.Write(entity, pointers.Capture(untyped, (nuint)memory));
            Assert.Same(pointers, fields["Value"].PointerRegistry);
            for (int i = 0; i < 2000; i++) { fields["Value"].Pointer.Write(entity, token); _ = fields["Value"].Pointer.Read(entity); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { fields["Value"].Pointer.Write(entity, token); _ = fields["Value"].Pointer.Read(entity); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            pointers.Revoke(domain);
            Assert.Throws<InvalidOperationException>(() => fields["Value"].Pointer.Read(entity));
            Assert.Throws<InvalidOperationException>(() => fields["Value"].Pointer.Write(entity, token));
            fields["Value"].Pointer.Write(entity, default); Assert.True(fields["Value"].Pointer.Read(entity).IsNull);
        }
        finally { pointers.Revoke(domain); NativeMemory.Free(memory); world.Destroy(); }
    }
}
