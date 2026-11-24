using System.Globalization;
using Karpik.Engine.Shared.Modding.Lua;
using MoonSharp.Interpreter;
using Xunit;
using Xunit.Abstractions;

[Collection("Lua ECS allocation probes")]
public sealed class LuaExactValuesTests(ITestOutputHelper output)
{
    private static Script CreateScript()
    {
        var script = new Script(CoreModules.Preset_HardSandbox);
        LuaExactValues.Register(script);
        return script;
    }

    [Theory]
    [InlineData("i64", "-9223372036854775808")]
    [InlineData("i64", "9223372036854775807")]
    [InlineData("u64", "18446744073709551615")]
    [InlineData("decimal", "79228162514264337593543950335")]
    [InlineData("decimal", "-0.0000000000000000000000000001")]
    [InlineData("decimal", "0.1000000000000000000000000001")]
    public void ExactStringValuesRoundTrip(string kind, string text)
    {
        var script = CreateScript();
        script.Globals.Set("text", DynValue.NewString(text));
        var value = script.DoString($"local x=types.{kind}(text); assert(types.{kind}(tostring(x))==x); return tostring(x)");
        Assert.Equal(text, value.String);
    }

    [Fact]
    public void ExactArithmeticUsesCheckedTypedSemanticsAndImmutableValues()
    {
        var script = CreateScript();
        script.DoString("""
            local a=types.i64('7'); local b=types.i64('2'); local original=a
            assert(tostring(a+b)=='9' and tostring(a-b)=='5' and tostring(a*b)=='14')
            assert(tostring(a/b)=='3' and tostring(a%b)=='1' and tostring(-a)=='-7')
            assert(tostring(types.i64('-7')/b)=='-3' and tostring(types.i64('-7')%b)=='-1')
            assert(a>b and a>=b and b<a and b<=a and a~=b and a==types.i64('7'))
            a=a+b; assert(tostring(original)=='7' and tostring(a)=='9')
            assert(types.i64('1')~=types.u64('1') and types.i64('1')~=types.decimal('1'))
            assert(types.i64('1')~=1 and types.i64('1')~='1')
            assert(tostring(types.u64('18446744073709551614')+types.u64('1'))=='18446744073709551615')
            assert(tostring(types.decimal('0.1')+types.decimal('0.2'))=='0.3')
            assert(tostring(-types.u64('0'))=='0')
            """);
    }

    [Theory]
    [InlineData("types.i64('9223372036854775807')+types.i64('1')")]
    [InlineData("types.i64('-9223372036854775808')-types.i64('1')")]
    [InlineData("-types.i64('-9223372036854775808')")]
    [InlineData("types.i64('-9223372036854775808')/types.i64('-1')")]
    [InlineData("types.u64('18446744073709551615')+types.u64('1')")]
    [InlineData("types.u64('0')-types.u64('1')")]
    [InlineData("-types.u64('1')")]
    [InlineData("types.decimal('79228162514264337593543950335')+types.decimal('1')")]
    [InlineData("types.i64('1')/types.i64('0')")]
    [InlineData("types.decimal('1')/types.decimal('0')")]
    [InlineData("types.i64('1')+types.u64('1')")]
    [InlineData("types.i64('1')<types.u64('1')")]
    [InlineData("types.i64('1')+1")]
    [InlineData("1+types.i64('1')")]
    [InlineData("types.i64('1')+'1'")]
    [InlineData("types.i64('2')^types.i64('3')")]
    public void InvalidArithmeticFailsWithoutDoubleFallback(string expression)
    {
        var script = CreateScript();
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return " + expression));
    }

    [Theory]
    [InlineData("types.i64('9223372036854775808')")]
    [InlineData("types.u64('-1')")]
    [InlineData("types.u64('18446744073709551616')")]
    [InlineData("types.i64(1.5)")]
    [InlineData("types.i64(9223372036854775808)")]
    [InlineData("types.u64(18446744073709551616)")]
    [InlineData("types.i64(types.decimal('1.1'))")]
    [InlineData("types.u64(types.i64('-1'))")]
    [InlineData("types.i64(types.u64('18446744073709551615'))")]
    [InlineData("types.decimal(0.1)")]
    [InlineData("types.decimal(79228162514264337593543950335)")]
    [InlineData("types.decimal(0.00000000186264514923095703125)")]
    [InlineData("types.decimal('0.00000000000000000000000000001')")]
    [InlineData("types.decimal('79228162514264337593543950336')")]
    [InlineData("types.decimal('0.10000000000000000000000000001')")]
    [InlineData("types.i64(true)")]
    [InlineData("types.i64(nil)")]
    [InlineData("types.i64(' 1')")]
    [InlineData("types.i64('1e2')")]
    [InlineData("types.decimal('1e2')")]
    [InlineData("types.i64(0/0)")]
    [InlineData("types.decimal(1/0)")]
    public void InvalidConstructorsRejectRangeFractionsAndInexactConversions(string expression)
    {
        var script = CreateScript();
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("return " + expression));
    }

    [Fact]
    public void ExplicitConversionsPreserveAlreadyRepresentedNumbers()
    {
        var script = CreateScript();
        script.DoString("""
            assert(tostring(types.i64(9007199254740992))=='9007199254740992')
            assert(tostring(types.i64(9007199254740993))=='9007199254740992')
            assert(tostring(types.i64(-9223372036854775808))=='-9223372036854775808')
            assert(tostring(types.u64(9223372036854775808))=='9223372036854775808')
            assert(tostring(types.i64(types.decimal('42.000')))=='42')
            assert(tostring(types.u64(types.i64('42')))=='42')
            assert(tostring(types.decimal(types.u64('18446744073709551615')))=='18446744073709551615')
            assert(types.decimal(0.5)==types.decimal('0.5'))
            assert(types.decimal(0.125)==types.decimal('0.125'))
            assert(types.decimal(0)==types.decimal('0'))
            assert(types.decimal(-0.125)==types.decimal('-0.125'))
            assert(types.decimal(0.0000000037252902984619140625)==types.decimal('0.0000000037252902984619140625'))
            assert(types.decimal('0.10000000000000000000000000000')==types.decimal('0.1'))
            """);
    }

    [Fact]
    public void DirectCreationAndStrictReadsPreserveKinds()
    {
        Assert.Equal(long.MinValue, LuaExactValues.ReadI64(LuaExactValues.Create(long.MinValue)));
        Assert.Equal(ulong.MaxValue, LuaExactValues.ReadU64(LuaExactValues.Create(ulong.MaxValue)));
        Assert.Equal(decimal.MaxValue, LuaExactValues.ReadDecimal(LuaExactValues.Create(decimal.MaxValue)));
        Assert.Throws<ScriptRuntimeException>(() => LuaExactValues.ReadI64(LuaExactValues.Create(1UL)));
        Assert.Throws<ScriptRuntimeException>(() => LuaExactValues.ReadDecimal(DynValue.NewNumber(0.5)));
        Assert.Throws<ScriptRuntimeException>(() => LuaExactValues.ReadU64(DynValue.Nil));
    }

    [Fact]
    public void GeneratedIntegerCasesMatchClrOracle()
    {
        var script = CreateScript();
        var operation = script.DoString("return function(a,b) return a+b,a-b,a*b,a/b,a%b,a<b end");
        var random = new Random(504120);
        for (int i = 0; i < 250; i++)
        {
            long a = random.NextInt64(-1000000000L, 1000000000L);
            long b = random.NextInt64(-1000000L, 1000000L);
            if (b == 0) b = 1;
            var actual = script.Call(operation, LuaExactValues.Create(a), LuaExactValues.Create(b)).Tuple;
            Assert.Equal(a + b, LuaExactValues.ReadI64(actual[0]));
            Assert.Equal(a - b, LuaExactValues.ReadI64(actual[1]));
            Assert.Equal(a * b, LuaExactValues.ReadI64(actual[2]));
            Assert.Equal(a / b, LuaExactValues.ReadI64(actual[3]));
            Assert.Equal(a % b, LuaExactValues.ReadI64(actual[4]));
            Assert.Equal(a < b, actual[5].Boolean);
        }
    }

    [Fact]
    public void ExactAdditionFitsExperimentalOperationAllocationCeiling()
    {
        foreach (string kind in new[] { "i64", "u64", "decimal" })
        {
            var script = CreateScript();
            var loop = script.DoString($"local first=types.{kind}('7');local step=types.{kind}('2');return function(n) local value=first for i=1,n do value=value+step end return value end");
            foreach (int count in new[] { 0, 1, 16, 256, 4096 })
            {
                DynValue[] arguments = [DynValue.NewNumber(count)];
                for (int warm = 0; warm < 64; warm++) script.Call(loop, arguments);
                const int repetitions = 50;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < repetitions; i++) script.Call(loop, arguments);
                long actual = (GC.GetAllocatedBytesForCurrentThread() - before) / repetitions;
                long ceiling = 4096L + 544L * count;
                output.WriteLine($"Exact-add {kind}, N={count}: {actual} B/callback, ceiling={ceiling}.");
                Assert.True(actual <= ceiling, $"{kind}, N={count}: {actual} B/callback exceeds {ceiling}.");
                var result = script.Call(loop, arguments);
                Assert.Equal((7L + 2L * count).ToString(CultureInfo.InvariantCulture), result.UserData.Descriptor.AsString(result.UserData.Object));
            }
        }
    }

    [Fact]
    public void FormattingAndParsingAreCultureIndependent()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var script = CreateScript();
            Assert.Equal("0.125", script.DoString("return tostring(types.decimal('0.125'))").String);
            Assert.Throws<ScriptRuntimeException>(() => script.DoString("return types.decimal('0,125')"));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
