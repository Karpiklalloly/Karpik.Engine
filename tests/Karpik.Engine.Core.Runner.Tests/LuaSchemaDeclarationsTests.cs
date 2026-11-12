using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using MoonSharp.Interpreter;
using Xunit;

public sealed class LuaSchemaDeclarationsTests
{
    [Fact]
    public void LuaSchemaDeclarations_PreservesAllExistingScalarTokens()
    {
        var script = new Script(CoreModules.Preset_HardSandbox); var table = new Table(script);
        (string Token, SchemaScalarKind Kind)[] kinds = [
            ("bool",SchemaScalarKind.Bool),("i8",SchemaScalarKind.I8),("u8",SchemaScalarKind.U8),
            ("i16",SchemaScalarKind.I16),("u16",SchemaScalarKind.U16),("i32",SchemaScalarKind.I32),
            ("u32",SchemaScalarKind.U32),("i64",SchemaScalarKind.I64),("u64",SchemaScalarKind.U64),
            ("f32",SchemaScalarKind.F32),("f64",SchemaScalarKind.F64),("char",SchemaScalarKind.Char),
            ("decimal",SchemaScalarKind.Decimal),("nint",SchemaScalarKind.NInt),("nuint",SchemaScalarKind.NUInt)];
        foreach (var kind in kinds) table.Set(kind.Token, DynValue.NewString(kind.Token));
        var fields = LuaSchemaDeclarations.ReadFields(table, "Component 'test:All'");
        Assert.Equal(kinds.Length, fields.Length);
        foreach (var kind in kinds)
        {
            var field = fields.Single(f => f.Name == kind.Token);
            Assert.Equal(kind.Kind, field.Kind); Assert.Null(field.Value);
        }
    }

    [Fact]
    public void LuaSchemaDeclarations_ReadsNestedFixedEnumAndPointerNodes()
    {
        var fields = Read("""
            return {
                Position={kind='struct',fields={X='f32',Y='f64'}},
                Points={kind='fixed',element={kind='struct',fields={X='i32'}},length=3},
                Codes={kind='fixed',element='char',length=4},
                State={kind='enum',underlying='u64'},
                Data={kind='pointer',type='host:Buffer'},
                Flags={kind='fixed',element={kind='enum',underlying='u32'},length=2},
                Pointers={kind='fixed',element={kind='pointer',type='host:Buffer'},length=2}
            }
            """);
        Assert.Equal(7, fields.Length);
        var position = Node("Position"); Assert.Equal(SchemaValueKind.Struct, position.NodeKind);
        Assert.Equal(SchemaScalarKind.F64, position.Fields.Single(f => f.Name == "Y").Kind);
        var points = Node("Points"); Assert.Equal(SchemaValueKind.Fixed, points.NodeKind); Assert.Equal(3, points.Length);
        Assert.Equal(SchemaValueKind.Struct, points.Element!.NodeKind);
        Assert.Equal(SchemaScalarKind.Char, Node("Codes").Element!.ScalarKind);
        Assert.Equal(SchemaValueKind.Enum, Node("State").NodeKind); Assert.Equal(SchemaScalarKind.U64, Node("State").ScalarKind);
        Assert.Equal("host:Buffer", Node("Data").PointerTypeId);
        Assert.Equal(SchemaValueKind.Enum, Node("Flags").Element!.NodeKind);
        Assert.Equal(SchemaValueKind.Pointer, Node("Pointers").Element!.NodeKind);
        SchemaValue Node(string name) => fields.Single(f => f.Name == name).Value!;
    }

    [Fact]
    public void LuaSchemaDeclarations_RejectsUnknownOrMalformedDescriptorOptionsWithContext()
    {
        string[] bad = [
            "{Value='string'}", "{Value=42}", "{Value=true}", "{[1]='i32'}", "{[' ']='i32'}", "{}",
            "{Value={kind='unknown'}}", "{Value={kind=1}}", "{Value={fields={X='i32'}}}",
            "{Value={kind='struct',fields={X='i32'},extra=true}}", "{Value={kind='struct',fields=1}}",
            "{Value={kind='struct',fields={}}}", "{Value={kind='struct',fields={X='i32'},[1]=true}}",
            "{Value={kind='fixed',element='f32',length=2,fields={X='i32'}}}",
            "{Value={kind='fixed',element={kind='fixed',element='f32',length=2},length=2}}",
            "{Value={kind='enum',underlying='f32'}}", "{Value={kind='enum',underlying='nint'}}",
            "{Value={kind='enum',underlying='u32',members={A=1}}}",
            "{Value={kind='pointer',type=' '}}", "{Value={kind='pointer',type=1}}",
            "{Value={kind='pointer',type='host:Buffer',address=16}}"
        ];
        foreach (string source in bad)
        {
            var error = Assert.Throws<InvalidDataException>(() => Read("return " + source));
            Assert.Contains("test:Shape", error.Message);
        }
        var nested = Assert.Throws<InvalidDataException>(() => Read("return {Position={kind='struct',fields={X='bad'}}}"));
        Assert.Contains("Position", nested.Message); Assert.Contains("X", nested.Message);
    }

    [Fact]
    public void LuaSchemaDeclarations_RejectsInvalidFixedLengthsAndElementShapes()
    {
        foreach (double length in new[] { 0d,-1d,1.5d,double.NaN,double.PositiveInfinity,(double)int.MaxValue+1 })
        {
            var script = new Script(); var fields = new Table(script); var descriptor = new Table(script);
            descriptor.Set("kind", DynValue.NewString("fixed")); descriptor.Set("element", DynValue.NewString("i32"));
            descriptor.Set("length", DynValue.NewNumber(length)); fields.Set("Values", DynValue.NewTable(descriptor));
            var error = Assert.Throws<InvalidDataException>(() => LuaSchemaDeclarations.ReadFields(fields, "Component 'test:Shape'"));
            Assert.Contains("Values", error.Message);
        }
        foreach (string source in new[] { "{kind='fixed',length=2}", "{kind='fixed',element=3,length=2}", "{kind='fixed',element='f32',length='2'}" })
            Assert.Throws<InvalidDataException>(() => Read("return {Values=" + source + "}"));
    }

    [Fact]
    public void LuaSchemaDeclarations_DetectsCyclesButAllowsSharedAcyclicDescriptions()
    {
        var script = new Script(); var fields = new Table(script); var descriptor = new Table(script);
        descriptor.Set("kind", DynValue.NewString("struct")); descriptor.Set("fields", DynValue.NewTable(fields));
        fields.Set("Again", DynValue.NewTable(descriptor));
        var error = Assert.Throws<InvalidDataException>(() => LuaSchemaDeclarations.ReadFields(fields, "Component 'test:Shape'"));
        Assert.Contains("cyclic", error.Message, StringComparison.OrdinalIgnoreCase);
        var valid = Read("local point={kind='struct',fields={X='f32'}} return {A=point,B=point}");
        Assert.Equal(2, valid.Length); Assert.Same(valid[0].Value, valid[1].Value);
    }

    [Fact]
    public void LuaSchemaDeclarations_DeepDeclarationsUseNoRecursiveClrStack()
    {
        const int depth = 6000;
        var script = new Script(); var fields = new Table(script); fields.Set("Leaf", DynValue.NewString("i32"));
        for (int i = 0; i < depth; i++)
        {
            var value = new Table(script); value.Set("kind", DynValue.NewString("struct")); value.Set("fields", DynValue.NewTable(fields));
            fields = new Table(script); fields.Set("Next", DynValue.NewTable(value));
        }
        var result = LuaSchemaDeclarations.ReadFields(fields, "Component 'test:Shape'");
        for (int i = 0; i < depth; i++) result = Assert.Single(result).Value!.Fields;
        Assert.Equal(SchemaScalarKind.I32, Assert.Single(result).Kind);
    }

    private static SchemaField[] Read(string source)
    {
        var script = new Script(CoreModules.Preset_HardSandbox);
        return LuaSchemaDeclarations.ReadFields(script.DoString(source).Table, "Component 'test:Shape'");
    }
}
