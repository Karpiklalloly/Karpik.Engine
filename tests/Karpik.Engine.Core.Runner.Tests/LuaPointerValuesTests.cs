using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using Xunit;

public sealed class LuaPointerValuesTests
{
    [Fact]
    public void LuaPointerValues_NullMapsToNilAndStillChecksTargetPermission()
    {
        using var registry = new ComponentPointerRegistry();
        var type = Register(registry);
        Assert.Equal(DataType.Nil, LuaPointerValues.Create(default, registry).Type);
        Assert.True(LuaPointerValues.Read(DynValue.Nil, registry, type).IsNull);
        Assert.Throws<ScriptRuntimeException>(() => LuaPointerValues.Read(DynValue.Nil, registry, default));
    }

    [Fact]
    public void LuaPointerValues_RoundTripRequiresOwnOpaqueWrapperAndCompatibleType()
    {
        using var registry = new ComponentPointerRegistry(); var type = Register(registry);
        var other = registry.RegisterType("host:Other", typeof(void).MakePointerType(), registry.CreateDomain());
        var token = registry.Capture(type, 16);
        DynValue value = LuaPointerValues.Create(token, registry);
        Assert.Equal(DataType.UserData, value.Type);
        Assert.Equal(token, LuaPointerValues.Read(value, registry, type));
        Assert.Throws<ScriptRuntimeException>(() => LuaPointerValues.Read(value, registry, other));
        DynValue[] invalid = [DynValue.NewNumber(16), DynValue.NewString("16"), DynValue.True,
            DynValue.NewTable(new Script()), UserData.Create(new object(), new ImpostorDescriptor())];
        foreach (var candidate in invalid)
            Assert.Throws<ScriptRuntimeException>(() => LuaPointerValues.Read(candidate, registry, type));
    }

    [Fact]
    public void LuaPointerValues_RejectForeignRegistryEvenWithSameTypeName()
    {
        using var first = new ComponentPointerRegistry(); using var second = new ComponentPointerRegistry();
        var firstType = Register(first); var secondType = Register(second);
        var token = first.Capture(firstType, 16); var value = LuaPointerValues.Create(token, first);
        Assert.Throws<ScriptRuntimeException>(() => LuaPointerValues.Create(token, second));
        Assert.Throws<ScriptRuntimeException>(() => LuaPointerValues.Read(value, second, secondType));
    }

    [Fact]
    public void LuaPointerValues_ExposeNoMembersAddressOrArithmetic()
    {
        using var registry = new ComponentPointerRegistry(); var type = Register(registry);
        var script = new Script(CoreModules.Preset_HardSandbox);
        script.Globals.Set("p", LuaPointerValues.Create(registry.Capture(type, 0x12345), registry));
        Assert.Equal("opaque pointer", script.DoString("return tostring(p)").String);
        foreach (string expression in new[] { "p.Address", "p.Registry", "p[1]", "p + 1", "-p", "p .. ''", "p.Address = 0" })
        {
            string code = expression.Contains(" = ", StringComparison.Ordinal) ? expression : "return " + expression;
            var error = Assert.Throws<ScriptRuntimeException>(() => script.DoString(code));
            Assert.DoesNotContain("74565", error.Message);
            Assert.DoesNotContain("12345", error.Message);
        }
        Assert.Equal(DataType.Nil, script.DoString("return tonumber(p)").Type);
    }

    [Fact]
    public void LuaPointerValues_RevocationInvalidatesRetainedWrapperAndTostring()
    {
        using var registry = new ComponentPointerRegistry(); var domain = registry.CreateDomain();
        var type = registry.RegisterType("host:Buffer", typeof(int).MakePointerType(), domain);
        var value = LuaPointerValues.Create(registry.Capture(type, 16), registry);
        var script = new Script(CoreModules.Preset_HardSandbox); script.Globals.Set("p", value);
        registry.Revoke(domain);
        Assert.Throws<ScriptRuntimeException>(() => LuaPointerValues.Read(value, registry, type));
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return tostring(p)"));
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return p.Address"));
        Assert.True(LuaPointerValues.Read(DynValue.Nil, registry, type).IsNull);
    }

    [Fact]
    public void LuaPointerValues_DisposalInvalidatesRetainedWrapperIncludingNullAccess()
    {
        var registry = new ComponentPointerRegistry(); var type = Register(registry);
        var value = LuaPointerValues.Create(registry.Capture(type, 16), registry);
        var script = new Script(CoreModules.Preset_HardSandbox); script.Globals.Set("p", value);
        registry.Dispose();
        Assert.Throws<ScriptRuntimeException>(() => LuaPointerValues.Create(default, registry));
        Assert.Throws<ScriptRuntimeException>(() => LuaPointerValues.Read(DynValue.Nil, registry, type));
        Assert.Throws<ScriptRuntimeException>(() => LuaPointerValues.Read(value, registry, type));
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return tostring(p)"));
    }

    [Fact]
    public void LuaPointerValues_ReadingPreparedWrapperAllocatesZero()
    {
        using var registry = new ComponentPointerRegistry(); var type = Register(registry);
        var value = LuaPointerValues.Create(registry.Capture(type, 16), registry); registry.Freeze();
        for (int i = 0; i < 2000; i++) LuaPointerValues.Read(value, registry, type);
        long before = GC.GetAllocatedBytesForCurrentThread();
        ComponentPointerValue result = default;
        for (int i = 0; i < 1000; i++) result = LuaPointerValues.Read(value, registry, type);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal((nuint)16, registry.ReadAddress(result, type));
    }

    private static ComponentPointerType Register(ComponentPointerRegistry registry) =>
        registry.RegisterType("host:Buffer", typeof(int).MakePointerType(), registry.CreateDomain());

    private sealed class ImpostorDescriptor : IUserDataDescriptor
    {
        public string Name => "Impostor";
        public Type Type => typeof(object);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing) => DynValue.Nil;
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing) => false;
        public string AsString(object obj) => "impostor";
        public DynValue MetaIndex(Script script, object obj, string metaname) => null!;
        public bool IsTypeCompatible(Type type, object obj) => type == typeof(object);
    }
}
