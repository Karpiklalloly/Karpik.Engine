using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Microsoft.Extensions.Logging.Abstractions;
using MoonSharp.Interpreter;
using Xunit;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;

public sealed class ScalarCatalogTests
{
    public enum Signed8 : sbyte { Zero }
    public enum Unsigned8 : byte { Zero }
    public enum Signed16 : short { Zero }
    public enum Unsigned16 : ushort { Zero }
    public enum Signed32 : int { Zero }
    public enum Unsigned32 : uint { Zero }
    public enum Signed64 : long { Zero }
    public enum Unsigned64 : ulong { Zero }
    public struct CatalogScalars : IEcsComponent
    {
        public sbyte I8; public byte U8; public short I16; public ushort U16; public char Char;
        public long I64; public ulong U64; public decimal Decimal; public nint NInt; public nuint NUInt;
        public Signed8 E8; public Unsigned8 EU8; public Signed16 E16; public Unsigned16 EU16;
        public Signed32 E32; public Unsigned32 EU32; public Signed64 E64; public Unsigned64 EU64;
        public readonly long ReadOnly;
    }
    public struct KeepAlive : IEcsComponent;

    private static readonly (string Name, SchemaScalarKind Kind)[] Kinds =
    [
        ("Char", SchemaScalarKind.Char), ("Decimal", SchemaScalarKind.Decimal),
        ("I16", SchemaScalarKind.I16), ("I64", SchemaScalarKind.I64), ("I8", SchemaScalarKind.I8),
        ("NInt", SchemaScalarKind.NInt), ("NUInt", SchemaScalarKind.NUInt),
        ("U16", SchemaScalarKind.U16), ("U64", SchemaScalarKind.U64), ("U8", SchemaScalarKind.U8)
    ];

    [Fact]
    public void DynamicCatalog_PreservesTypedValuesAcrossGrowthSwapAndDefaultReuse()
    {
        var schemas = new ComponentSchemaRegistry();
        schemas.ValidateAndFreeze([new("game:Catalog", 1, Kinds.Select(k => new SchemaField(k.Name, k.Kind)).ToArray())]);
        var world = new EcsDefaultWorld();
        using var storage = new DynamicComponentStorage(world, schemas);
        try
        {
            var pool = storage.Resolve("game:Catalog"); var fields = pool.Fields.ToArray();
            int first = world.NewEntity(); world.GetPool<KeepAlive>().Add(first); pool.Add(first);
            WriteFields(fields, first);
            int last = first;
            for (int i = 0; i < 257; i++) { last = world.NewEntity(); pool.Add(last); }
            WriteFields(fields, last); AssertFields(fields, first); AssertFields(fields, last);
            pool.Del(first); AssertFields(fields, last);
            pool.Add(first);
            Assert.Equal('\0', fields[0].Char.Read(first)); Assert.Equal(0m, fields[1].Decimal.Read(first));
            Assert.Equal((short)0, fields[2].I16.Read(first)); Assert.Equal(0L, fields[3].I64.Read(first));
            Assert.Equal((sbyte)0, fields[4].I8.Read(first)); Assert.Equal((nint)0, fields[5].NInt.Read(first));
            Assert.Equal((nuint)0, fields[6].NUInt.Read(first)); Assert.Equal((ushort)0, fields[7].U16.Read(first));
            Assert.Equal(0UL, fields[8].U64.Read(first)); Assert.Equal((byte)0, fields[9].U8.Read(first));
            storage.Dispose(); Assert.Throws<ObjectDisposedException>(() => fields[3].I64.Read(last));
        }
        finally { world.Destroy(); }
    }

    [Fact]
    public void PreparedCatalog_PreservesOwnerReadonlyAndNoAllocationInTypedAccess()
    {
        var schemas = new ComponentSchemaRegistry();
        schemas.ValidateAndFreeze([new("game:Catalog", 1, Kinds.Select(k => new SchemaField(k.Name, k.Kind)).ToArray())]);
        var world = new EcsDefaultWorld(); using var scope = new ComponentCallbackScope();
        using var queries = new ComponentQueryPreparer(world, new ClrComponentBindings([]), schemas, scope);
        queries.Initialize();
        try
        {
            var writable = queries.Prepare(new("q", [], [], [new("game:Catalog", false)]), "owner").Components["game:Catalog"];
            var readOnly = queries.Prepare(new("qro", [], [], [new("game:Catalog")]), "owner").Components["game:Catalog"];
            int entity = world.NewEntity(); scope.Enter("owner");
            var v = writable.Add(entity); var ro = readOnly.Get(entity);
            Assert.Throws<InvalidOperationException>(() => ro.WriteI64(3, 1));
            Assert.Throws<InvalidOperationException>(() => v.ReadI64(0));
            for (int i = 0; i < 2000; i++) Run();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) Run();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, bytes); Assert.Equal(long.MinValue, v.ReadI64(3));
            Assert.Equal(ulong.MaxValue, v.ReadU64(8)); Assert.Equal(decimal.MaxValue, v.ReadDecimal(1));
            scope.Exit(); Assert.Throws<InvalidOperationException>(() => v.ReadNInt(5));
            scope.Enter("other"); Assert.Throws<InvalidOperationException>(() => v.ReadChar(0)); scope.Exit();
            scope.Enter("owner"); queries.Dispose(); Assert.Throws<ObjectDisposedException>(() => v.ReadDecimal(1)); scope.Exit();
            void Run()
            {
                v.WriteChar(0, '\ud800'); v.WriteDecimal(1, decimal.MaxValue); v.WriteI16(2, short.MinValue);
                v.WriteI64(3, long.MinValue); v.WriteI8(4, sbyte.MinValue); v.WriteNInt(5, nint.MinValue);
                v.WriteNUInt(6, nuint.MaxValue); v.WriteU16(7, ushort.MaxValue); v.WriteU64(8, ulong.MaxValue);
                v.WriteU8(9, byte.MaxValue);
                _ = v.ReadChar(0); _ = v.ReadDecimal(1); _ = v.ReadI16(2); _ = v.ReadI64(3); _ = v.ReadI8(4);
                _ = v.ReadNInt(5); _ = v.ReadNUInt(6); _ = v.ReadU16(7); _ = v.ReadU64(8); _ = v.ReadU8(9);
            }
        }
        finally { world.Destroy(); }
    }

    [Fact]
    public void GeneratedCatalog_UsesAllEnumUnderlyingTypesAndReadOnlyGuards()
    {
        var bindings = new ClrComponentBindings([new GeneratedBindings()]); bindings.Register<CatalogScalars>("game:Catalog");
        var world = new EcsDefaultWorld();
        try
        {
            var pool = bindings.ResolveBinding("game:Catalog").Bind(world); int entity = world.NewEntity(); pool.Add(entity);
            var f = pool.Fields.ToArray().ToDictionary(field => field.Name, StringComparer.Ordinal);
            f["E8"].I8.Write(entity, -117); f["EU8"].U8.Write(entity, 250);
            f["E16"].I16.Write(entity, -30001); f["EU16"].U16.Write(entity, 60001);
            f["E32"].I32.Write(entity, int.MinValue); f["EU32"].U32.Write(entity, uint.MaxValue);
            f["E64"].I64.Write(entity, long.MinValue); f["EU64"].U64.Write(entity, ulong.MaxValue);
            Assert.False(f["ReadOnly"].CanWrite); Assert.Throws<InvalidOperationException>(() => f["ReadOnly"].I64.Write(entity, 1));
            ref var v = ref world.GetPool<CatalogScalars>().Get(entity);
            Assert.Equal((Signed8)(-117), v.E8); Assert.Equal((Unsigned8)250, v.EU8);
            Assert.Equal((Signed16)(-30001), v.E16); Assert.Equal((Unsigned16)60001, v.EU16);
            Assert.Equal((Signed32)int.MinValue, v.E32); Assert.Equal((Unsigned32)uint.MaxValue, v.EU32);
            Assert.Equal((Signed64)long.MinValue, v.E64); Assert.Equal((Unsigned64)ulong.MaxValue, v.EU64);
            Assert.Equal(long.MinValue, f["E64"].I64.Read(entity)); Assert.Equal(ulong.MaxValue, f["EU64"].U64.Read(entity));
        }
        finally { world.Destroy(); }
    }

    [Fact]
    public void LuaCatalog_RoundTripsAllNewScalarsNativeAndDynamicWithoutLoss()
    {
        const string body = """
            v.I8=p.I8; v.U8=p.U8; v.I16=p.I16; v.U16=p.U16; v.Char=p.Char;
            v.I64=p.I64; v.U64=p.U64; v.Decimal=p.Decimal; v.NInt=p.NInt; v.NUInt=p.NUInt;
            assert(tostring(v.I64)=='-9223372036854775808')
            assert(tostring(v.U64)=='18446744073709551615')
            assert(v.Decimal == types.decimal('0.1000000000000000000000000001'))
            p.I64=v.I64+types.i64('1'); p.U64=v.U64-types.u64('1');
            p.Decimal=v.Decimal+types.decimal('1');
            """;
        using var f = new LuaFixture(body); int entity = f.Entity();
        f.Runtime.InvokeSystem(0, 0.02);
        ref var native = ref f.World.GetPool<CatalogScalars>().Get(entity);
        Assert.Equal(long.MinValue + 1, native.I64); Assert.Equal(ulong.MaxValue - 1, native.U64);
        Assert.Equal(1.1000000000000000000000000001m, native.Decimal);
        f.Inspect(() =>
        {
            var fields = f.Values.Fields.ToArray(); AssertFields(fields, entity, 0.1000000000000000000000000001m);
        });
    }

    [Theory]
    [InlineData("I8", "-129")]
    [InlineData("I8", "128")]
    [InlineData("U8", "-1")]
    [InlineData("U8", "256")]
    [InlineData("I16", "-32769")]
    [InlineData("I16", "32768")]
    [InlineData("U16", "-1")]
    [InlineData("U16", "65536")]
    [InlineData("Char", "-1")]
    [InlineData("Char", "65536")]
    [InlineData("Char", "'x'")]
    [InlineData("I8", "1.5")]
    [InlineData("U8", "0/0")]
    [InlineData("I16", "1/0")]
    [InlineData("U16", "true")]
    [InlineData("I64", "1")]
    [InlineData("U64", "'1'")]
    [InlineData("Decimal", "0.1")]
    [InlineData("NInt", "1")]
    [InlineData("NUInt", "types.i64('1')")]
    public void LuaCatalog_RejectsInvalidWritesBeforeMutationWithFieldContext(string field, string expression)
    {
        using var f = new LuaFixture($"p.{field}={expression}"); int entity = f.Entity();
        Exception error = Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(0, 0.02));
        Assert.Contains(field, error.Message); Assert.Contains("game:Catalog", error.Message);
        var native = f.World.GetPool<CatalogScalars>().Get(entity);
        Assert.Equal(sbyte.MinValue, native.I8); Assert.Equal(byte.MaxValue, native.U8);
        Assert.Equal(short.MinValue, native.I16); Assert.Equal(ushort.MaxValue, native.U16);
        Assert.Equal('\ud800', native.Char); Assert.Equal(long.MinValue, native.I64); Assert.Equal(ulong.MaxValue, native.U64);
        Assert.Equal(0.1000000000000000000000000001m, native.Decimal);
        Assert.Equal(nint.MinValue, native.NInt); Assert.Equal(nuint.MaxValue, native.NUInt);
    }

    private sealed class LuaFixture : IDisposable
    {
        internal readonly Script Script = new(); internal readonly EcsDefaultWorld World = new();
        internal readonly ComponentCallbackScope Scope = new(); internal readonly LuaScriptRuntime Runtime;
        internal readonly ComponentQueryPreparer Preparer;
        internal LuaFixture(string body)
        {
            LuaExactValues.Register(Script);
            string source = """
                return {components={Values={version=1,fields={Char='char',Decimal='decimal',I16='i16',I64='i64',I8='i8',NInt='nint',NUInt='nuint',U16='u16',U64='u64',U8='u8'}}},
                systems={{id='copy',phase='update',queries={q={inc={['game:Catalog']={readonly=false}},exc={['catalog:Values']={readonly=false}}}},callback=function(dt,ctx)
                for entity in ctx.queries.q:entities() do
                    local p=ctx.queries.q.components['game:Catalog']:get(entity)
                    local v=ctx.queries.q.components['catalog:Values']:add(entity)
                    __BODY__
                end end}}}
                """.Replace("__BODY__", body, StringComparison.Ordinal);
            Runtime = new(Script, [Script.DoString(source).Table], "catalog", ["main.lua"]);
            var bindings = new ClrComponentBindings([new GeneratedBindings()]); bindings.Register<CatalogScalars>("game:Catalog");
            var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze(Runtime.Components);
            Preparer = new(World, bindings, schemas, Scope); Preparer.Initialize();
            Runtime.PrepareEcs(Preparer, Scope, new ScriptSystemOptions(), NullLogger.Instance);
        }
        internal PreparedComponentHandle Values => Runtime.GetContext(0).Queries[0].Components["catalog:Values"];
        internal int Entity()
        {
            int entity = World.NewEntity(); World.GetPool<CatalogScalars>().Add(entity) = new()
            {
                I8=sbyte.MinValue,U8=byte.MaxValue,I16=short.MinValue,U16=ushort.MaxValue,Char='\ud800',
                I64=long.MinValue,U64=ulong.MaxValue,Decimal=0.1000000000000000000000000001m,NInt=nint.MinValue,NUInt=nuint.MaxValue
            };
            return entity;
        }
        internal void Inspect(Action action) { Scope.Enter("catalog:copy"); try { action(); } finally { Scope.Exit(); } }
        public void Dispose() { Runtime.Dispose(); Preparer.Dispose(); Scope.Dispose(); World.Destroy(); }
    }

    private static void WriteFields(ComponentFieldDescriptor[] f, int e)
    {
        f[0].Char.Write(e, '\ud800'); f[1].Decimal.Write(e, decimal.MaxValue); f[2].I16.Write(e, short.MinValue);
        f[3].I64.Write(e, long.MinValue); f[4].I8.Write(e, sbyte.MinValue); f[5].NInt.Write(e, nint.MinValue);
        f[6].NUInt.Write(e, nuint.MaxValue); f[7].U16.Write(e, ushort.MaxValue); f[8].U64.Write(e, ulong.MaxValue); f[9].U8.Write(e, byte.MaxValue);
    }
    private static void AssertFields(ComponentFieldDescriptor[] f, int e, decimal expectedDecimal = decimal.MaxValue)
    {
        Assert.Equal('\ud800', f[0].Char.Read(e));
        Assert.Equal(expectedDecimal, f[1].Decimal.Read(e));
        Assert.Equal(short.MinValue, f[2].I16.Read(e)); Assert.Equal(long.MinValue, f[3].I64.Read(e));
        Assert.Equal(sbyte.MinValue, f[4].I8.Read(e)); Assert.Equal(nint.MinValue, f[5].NInt.Read(e));
        Assert.Equal(nuint.MaxValue, f[6].NUInt.Read(e)); Assert.Equal(ushort.MaxValue, f[7].U16.Read(e));
        Assert.Equal(ulong.MaxValue, f[8].U64.Read(e)); Assert.Equal(byte.MaxValue, f[9].U8.Read(e));
    }
}
