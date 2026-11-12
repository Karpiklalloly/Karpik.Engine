using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;

public sealed class CompositeBindingsTests
{
    private static ComponentSchema Description() => new("test:Shape", 1,
        new("Position", SchemaValue.Struct(new("X", SchemaScalarKind.F32), new("Y", SchemaScalarKind.F32))),
        new("Points", SchemaValue.Fixed(SchemaValue.Struct(new("X", SchemaScalarKind.I32), new("Y", SchemaScalarKind.F64)), 3)));

    [Fact]
    public void NestedFixedStorage_PreservesEveryElementThroughSwapAndGrowth()
    {
        var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze([Description()]);
        var world = new EcsDefaultWorld();
        try
        {
        using var storage = new DynamicComponentStorage(world, schemas);
        var pool = storage.Resolve("test:Shape");
        int a = world.NewEntity(), b = world.NewEntity(); pool.Add(a); pool.Add(b);
        var fields = pool.Fields.ToArray();
        Assert.Equal(8, fields.Length);
        int x = Array.FindIndex(fields, f => f.Name == "Points[3].X");
        int y = Array.FindIndex(fields, f => f.Name == "Position.Y");
        Assert.True(x >= 0); Assert.True(y >= 0);
        fields[x].I32.Write(b, 123); fields[y].F32.Write(b, 4.5f);
        pool.Del(a);
        for (int i = 0; i < 2048; i++) { int e = world.NewEntity(); pool.Add(e); }
        Assert.Equal(123, fields[x].I32.Read(b)); Assert.Equal(4.5f, fields[y].F32.Read(b));
        pool.Add(a); Assert.Equal(0, fields[x].I32.Read(a));
        }
        finally { world.Destroy(); }
    }

    [Fact]
    public void CompositeSchema_CanonicalOrderAndDefensiveCopiesCoverNestedTrees()
    {
        var schema = Description(); var registry = new ComponentSchemaRegistry(); registry.ValidateAndFreeze([schema]);
        var copy = registry.GetSchemas();
        copy[0].Fields[0].Value!.Element!.Fields[0] = new("Changed", SchemaScalarKind.Bool);
        Assert.Equal("X", registry.GetSchemas()[0].Fields[0].Value!.Element!.Fields[0].Name);
        registry.ValidateAndFreeze([Description() with { Fields = Description().Fields.Reverse().ToArray() }]);
        Assert.Throws<InvalidDataException>(() => registry.ValidateAndFreeze([new("test:Shape", 1,
            new SchemaField("Points", SchemaValue.Fixed(SchemaValue.Struct(new("X", SchemaScalarKind.I32), new("Y", SchemaScalarKind.F64)), 4)),
            new("Position", SchemaValue.Struct(new("X", SchemaScalarKind.F32), new("Y", SchemaScalarKind.F32))))]));
    }

    [Fact]
    public void CompositeSchema_RejectsCyclesAndOverflowBeforeStorage()
    {
        SchemaField[] children = new SchemaField[1];
        var cycle = SchemaValue.Struct(children); children[0] = new("Again", cycle);
        Assert.Throws<InvalidDataException>(() => new ComponentSchemaRegistry().ValidateAndFreeze([new("test:Cycle", 1, new SchemaField("Root", cycle))]));
        Assert.Throws<InvalidDataException>(() => new ComponentSchemaRegistry().ValidateAndFreeze([new("test:Zero", 1,
            new SchemaField("Array", SchemaValue.Fixed(SchemaValue.Scalar(SchemaScalarKind.F32), 0)))]));
        Assert.Throws<InvalidDataException>(() => new ComponentSchemaRegistry().ValidateAndFreeze([new("test:Overflow", 1,
            new SchemaField("Array", SchemaValue.Fixed(SchemaValue.Scalar(SchemaScalarKind.F64), int.MaxValue)))]));
    }
}
