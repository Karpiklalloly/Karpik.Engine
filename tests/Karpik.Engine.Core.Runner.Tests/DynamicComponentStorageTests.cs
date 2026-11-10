using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;

public sealed class DynamicComponentStorageTests
{
    private static ComponentSchema Temperature(string id = "weather:Temperature", int version = 1) =>
        new(id, version, new SchemaField("Value", SchemaScalarKind.F32));

    [Fact]
    public void DynamicComponent_ModSetFreezeAndSimulationIsolation()
    {
        var registry = new ComponentSchemaRegistry();
        Assert.Throws<InvalidDataException>(() => registry.ValidateAndFreeze([Temperature(), Temperature(version: 2)]));
        Assert.False(registry.IsFrozen);
        registry.ValidateAndFreeze([Temperature(), Temperature()]);
        registry.ValidateAndFreeze([Temperature()]);
        Assert.Throws<InvalidDataException>(() => registry.ValidateAndFreeze([Temperature(version: 2)]));
        Assert.Throws<InvalidDataException>(() => registry.ValidateAndFreeze([]));
        var firstWorld = new EcsDefaultWorld(); var secondWorld = new EcsDefaultWorld();
        using var first = new DynamicComponentStorage(firstWorld, registry);
        using var second = new DynamicComponentStorage(secondWorld, registry);
        try
        {
            int entity = firstWorld.NewEntity(); first.Resolve("weather:Temperature").Add(entity);
            first.Resolve("weather:Temperature").Fields.Span[0].F32.Write(entity, 30);
            Assert.Equal(0, second.Resolve("weather:Temperature").Count);
        }
        finally { firstWorld.Destroy(); secondWorld.Destroy(); }
    }

    [Fact]
    public void DynamicComponent_DefaultSwapRemoveAndSlotReuse()
    {
        var registry = new ComponentSchemaRegistry(); registry.ValidateAndFreeze([Temperature()]);
        var world = new EcsDefaultWorld();
        using var storage = new DynamicComponentStorage(world, registry);
        try
        {
            var keep = world.GetPool<KeepAlive>();
            var pool = storage.Resolve("weather:Temperature"); var field = pool.Fields.Span[0].F32;
            int a = world.NewEntity(); keep.Add(a); pool.Add(a); field.Write(a, 10);
            int b = world.NewEntity(); keep.Add(b); pool.Add(b); field.Write(b, 20);
            int c = world.NewEntity(); keep.Add(c); pool.Add(c); field.Write(c, 30);
            pool.Del(b);
            Assert.Equal(10, field.Read(a)); Assert.Equal(30, field.Read(c));
            pool.Add(b); Assert.Equal(0, field.Read(b));
            Assert.Throws<InvalidOperationException>(() => pool.Add(b));
            pool.Del(b); Assert.Throws<InvalidOperationException>(() => pool.Del(b));
            Assert.Throws<InvalidOperationException>(() => field.Read(b));
        }
        finally { world.Destroy(); }
    }

    [Fact]
    public void DynamicComponent_AnchorExistsIffAnySchemaPresent()
    {
        var registry = new ComponentSchemaRegistry(); registry.ValidateAndFreeze([Temperature(), Temperature("other:Heat")]);
        var world = new EcsDefaultWorld();
        using var storage = new DynamicComponentStorage(world, registry);
        try
        {
            var anchors = world.GetPool<DynamicComponentAnchor>();
            var keep = world.GetPool<KeepAlive>();
            int entity = world.NewEntity(); keep.Add(entity);
            var a = storage.Resolve("weather:Temperature"); var b = storage.Resolve("other:Heat");
            Assert.False(anchors.Has(entity)); a.Add(entity); b.Add(entity); keep.Del(entity);
            Assert.True(anchors.Has(entity)); world.ReleaseDelEntityBufferAll(); Assert.True(world.IsUsed(entity));
            a.Del(entity); Assert.True(anchors.Has(entity));
            b.Del(entity); Assert.False(anchors.Has(entity));
            world.ReleaseDelEntityBufferAll(); Assert.False(world.IsUsed(entity));
        }
        finally { world.Destroy(); }
    }

    [Fact]
    public void DynamicComponent_DeletionReleasePrecedesIdReuse()
    {
        var registry = new ComponentSchemaRegistry(); registry.ValidateAndFreeze([Temperature()]);
        var world = new EcsDefaultWorld();
        using var storage = new DynamicComponentStorage(world, registry);
        try
        {
            var pool = storage.Resolve("weather:Temperature");
            int a = world.NewEntity(); pool.Add(a);
            int b = world.NewEntity(); pool.Add(b);
            world.DelEntity(a); world.DelEntity(b);
            Assert.Throws<InvalidOperationException>(() => pool.Has(a));
            world.ReleaseDelEntityBuffer(1); Assert.Equal(1, pool.Count);
            world.ReleaseDelEntityBufferAll(); Assert.Equal(0, pool.Count);
            int reused = world.NewEntity(); Assert.Contains(reused, new[] { a, b });
            pool.Add(reused); Assert.Equal(0, pool.Fields.Span[0].F32.Read(reused));
        }
        finally { world.Destroy(); }
        storage.Dispose(); Assert.Throws<ObjectDisposedException>(() => storage.Resolve("weather:Temperature"));
    }

    [Fact]
    public void DynamicComponent_ResizePreservesData()
    {
        var registry = new ComponentSchemaRegistry(); registry.ValidateAndFreeze([Temperature()]);
        var world = new EcsDefaultWorld();
        using var storage = new DynamicComponentStorage(world, registry);
        try
        {
            var pool = storage.Resolve("weather:Temperature");
            int capacity = world.Capacity;
            var entities = new int[capacity + 10];
            for (int i = 0; i < entities.Length; i++)
            {
                int entity = world.NewEntity(); entities[i] = entity; pool.Add(entity); pool.Fields.Span[0].F32.Write(entity, i);
            }
            Assert.True(world.Capacity > capacity);
            for (int i = 0; i < entities.Length; i++) Assert.Equal(i, pool.Fields.Span[0].F32.Read(entities[i]));
        }
        finally { world.Destroy(); }
    }

    public struct KeepAlive : IEcsComponent;

    [Fact]
    public void DynamicComponent_RandomOperationsPreserveValuesAndAnchor()
    {
        var registry = new ComponentSchemaRegistry(); registry.ValidateAndFreeze([Temperature()]);
        var world = new EcsDefaultWorld();
        using var storage = new DynamicComponentStorage(world, registry);
        try
        {
            var keep = world.GetPool<KeepAlive>(); var anchors = world.GetPool<DynamicComponentAnchor>();
            var pool = storage.Resolve("weather:Temperature"); var field = pool.Fields.Span[0].F32;
            var entities = new int[32]; var present = new bool[32]; var values = new float[32];
            for (int i = 0; i < entities.Length; i++) { entities[i] = world.NewEntity(); keep.Add(entities[i]); }
            var random = new Random(20261025);
            for (int operation = 0; operation < 1000; operation++)
            {
                int index = random.Next(entities.Length); int entity = entities[index];
                if (!present[index]) { pool.Add(entity); present[index] = true; values[index] = 0; }
                else if (random.Next(2) == 0) { pool.Del(entity); present[index] = false; }
                else { values[index] = random.Next(1000); field.Write(entity, values[index]); }
                int expectedCount = 0;
                for (int i = 0; i < entities.Length; i++)
                {
                    Assert.Equal(present[i], pool.Has(entities[i])); Assert.Equal(present[i], anchors.Has(entities[i]));
                    if (present[i]) { expectedCount++; Assert.Equal(values[i], field.Read(entities[i])); }
                }
                Assert.Equal(expectedCount, pool.Count);
            }
        }
        finally { world.Destroy(); }
    }

    [Fact]
    public void DynamicComponent_FrozenSchemasDoNotShareCallerArrays()
    {
        var registry = new ComponentSchemaRegistry();
        var fields = new[] { new SchemaField("Value", SchemaScalarKind.F32) };
        registry.ValidateAndFreeze([new ComponentSchema("weather:Temperature", 1, fields)]);
        fields[0] = new SchemaField("Changed", SchemaScalarKind.F32);
        var snapshot = registry.GetSchemas(); snapshot[0].Fields[0] = fields[0];
        registry.ValidateAndFreeze([Temperature()]);
        Assert.Equal("Value", registry.GetSchemas()[0].Fields[0].Name);
    }
}
