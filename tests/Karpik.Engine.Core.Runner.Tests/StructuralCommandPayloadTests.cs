using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;

public sealed class StructuralCommandPayloadTests
{
    [Fact]
    public void StructuralCommand_AllScalarKindsRoundtripAndWarmedApplyAllocateZero()
    {
        var world=new EcsDefaultWorld(); using var scope=new ComponentCallbackScope();
        var schema=new ComponentSchema("test:All",1, Enum.GetValues<SchemaScalarKind>().Where(k=>k!=SchemaScalarKind.Pointer)
            .Select(k=>new SchemaField("K"+((int)k).ToString("D2"),k)).ToArray());
        var schemas=new ComponentSchemaRegistry(); schemas.ValidateAndFreeze([schema]);
        using var preparer=new ComponentQueryPreparer(world,new ClrComponentBindings([]),schemas,scope);
        try
        {
            preparer.Initialize(); var handle=preparer.Prepare(new("q",[],[],[new("test:All",false)]),"owner").Components["test:All"];
            var commands=preparer.GetCommands(new()); scope.Enter("owner");
            var snapshot=handle.NewSnapshot();
            for(int field=0;field<snapshot.Fields.Length;field++)
                switch(snapshot.GetFieldKind(field))
                {
                    case SchemaScalarKind.Bool: snapshot.Write<bool>(field,true); break;
                    case SchemaScalarKind.I8: snapshot.Write<sbyte>(field,sbyte.MinValue); break;
                    case SchemaScalarKind.U8: snapshot.Write<byte>(field,byte.MaxValue); break;
                    case SchemaScalarKind.I16: snapshot.Write<short>(field,short.MinValue); break;
                    case SchemaScalarKind.U16: snapshot.Write<ushort>(field,ushort.MaxValue); break;
                    case SchemaScalarKind.Char: snapshot.Write<char>(field,'\uFFFF'); break;
                    case SchemaScalarKind.I32: snapshot.Write<int>(field,int.MinValue); break;
                    case SchemaScalarKind.U32: snapshot.Write<uint>(field,uint.MaxValue); break;
                    case SchemaScalarKind.F32: snapshot.Write<float>(field,1.25f); break;
                    case SchemaScalarKind.F64: snapshot.Write<double>(field,Math.PI); break;
                    case SchemaScalarKind.I64: snapshot.Write<long>(field,long.MinValue); break;
                    case SchemaScalarKind.U64: snapshot.Write<ulong>(field,ulong.MaxValue); break;
                    case SchemaScalarKind.Decimal: snapshot.Write<decimal>(field,decimal.MaxValue); break;
                    case SchemaScalarKind.NInt: snapshot.Write<nint>(field,(nint)(-123)); break;
                    case SchemaScalarKind.NUInt: snapshot.Write<nuint>(field,(nuint)123); break;
                }
            int entity=world.NewEntity(); world.GetPool<StructuralCommandBufferTests.Keeper>().Add(entity);
            commands.Begin("owner"); commands.Add(handle,entity,snapshot); commands.Complete(); commands.End();
            var view=handle.Get(entity);
            for(int field=0;field<snapshot.Fields.Length;field++)
                switch(snapshot.GetFieldKind(field))
                {
                    case SchemaScalarKind.Bool: Assert.True(view.Read<bool>(field)); break;
                    case SchemaScalarKind.I8: Assert.Equal(sbyte.MinValue,view.Read<sbyte>(field)); break;
                    case SchemaScalarKind.U8: Assert.Equal(byte.MaxValue,view.Read<byte>(field)); break;
                    case SchemaScalarKind.I16: Assert.Equal(short.MinValue,view.Read<short>(field)); break;
                    case SchemaScalarKind.U16: Assert.Equal(ushort.MaxValue,view.Read<ushort>(field)); break;
                    case SchemaScalarKind.Char: Assert.Equal('\uFFFF',view.Read<char>(field)); break;
                    case SchemaScalarKind.I32: Assert.Equal(int.MinValue,view.Read<int>(field)); break;
                    case SchemaScalarKind.U32: Assert.Equal(uint.MaxValue,view.Read<uint>(field)); break;
                    case SchemaScalarKind.F32: Assert.Equal(1.25f,view.Read<float>(field)); break;
                    case SchemaScalarKind.F64: Assert.Equal(Math.PI,view.Read<double>(field)); break;
                    case SchemaScalarKind.I64: Assert.Equal(long.MinValue,view.Read<long>(field)); break;
                    case SchemaScalarKind.U64: Assert.Equal(ulong.MaxValue,view.Read<ulong>(field)); break;
                    case SchemaScalarKind.Decimal: Assert.Equal(decimal.MaxValue,view.Read<decimal>(field)); break;
                    case SchemaScalarKind.NInt: Assert.Equal((nint)(-123),view.Read<nint>(field)); break;
                    case SchemaScalarKind.NUInt: Assert.Equal((nuint)123,view.Read<nuint>(field)); break;
                }
            handle.Del(entity);
            for(int i=0;i<1000;i++) Cycle();
            long start=GC.GetAllocatedBytesForCurrentThread(); for(int i=0;i<1000;i++) Cycle();
            Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-start);
            void Cycle() { commands.Begin("owner"); commands.Add(handle,entity,snapshot); commands.Del(handle,entity); commands.Complete(); commands.End(); }
        }
        finally { scope.Exit(); preparer.Dispose(); world.Destroy(); }
    }

    [Fact]
    public void StructuralCommand_PointerRevocationAfterEnqueueRejectsWholeBatch()
    {
        var world=new EcsDefaultWorld(); using var scope=new ComponentCallbackScope(); using var pointers=new ComponentPointerRegistry();
        var domain=pointers.CreateDomain(); var type=pointers.RegisterType("host:Int",typeof(int).MakePointerType(),domain);
        var schemas=new ComponentSchemaRegistry(); schemas.ValidateAndFreeze([new ComponentSchema("test:Pointer",1,new SchemaField("Value",SchemaValue.Pointer("host:Int")))]);
        using var preparer=new ComponentQueryPreparer(world,new ClrComponentBindings([]),schemas,scope,pointers);
        try
        {
            preparer.Initialize(); var handle=preparer.Prepare(new("q",[],[],[new("test:Pointer",false)]),"owner").Components["test:Pointer"];
            var commands=preparer.GetCommands(new()); scope.Enter("owner"); var snapshot=handle.NewSnapshot();
            snapshot.WritePointer(0,pointers.Capture(type,16)); commands.Begin("owner");
            var pending=commands.Create(); commands.Add(handle,pending,snapshot); pointers.Revoke(domain);
            Assert.Throws<InvalidOperationException>(()=>commands.Complete()); Assert.Equal(0,world.Count); commands.End();
        }
        finally { scope.Exit(); preparer.Dispose(); world.Destroy(); }
    }

    [Fact]
    public void StructuralCommand_PointerPayloadAppliesAndWarmedCyclesAllocateZero()
    {
        var world=new EcsDefaultWorld(); using var scope=new ComponentCallbackScope(); using var pointers=new ComponentPointerRegistry();
        var type=pointers.RegisterType("host:Int",typeof(int).MakePointerType(),pointers.CreateDomain());
        var schemas=new ComponentSchemaRegistry(); schemas.ValidateAndFreeze([new ComponentSchema("test:Pointer",1,new SchemaField("Value",SchemaValue.Pointer("host:Int")))]);
        using var preparer=new ComponentQueryPreparer(world,new ClrComponentBindings([]),schemas,scope,pointers);
        try
        {
            preparer.Initialize(); var handle=preparer.Prepare(new("q",[],[],[new("test:Pointer",false)]),"owner").Components["test:Pointer"];
            var commands=preparer.GetCommands(new()); scope.Enter("owner"); var snapshot=handle.NewSnapshot();
            snapshot.WritePointer(0,pointers.Capture(type,16)); int entity=world.NewEntity(); world.GetPool<StructuralCommandBufferTests.Keeper>().Add(entity);
            commands.Begin("owner"); commands.Add(handle,entity,snapshot); commands.Complete(); commands.End();
            Assert.Equal((nuint)16,pointers.ReadAddress(handle.Get(entity).ReadPointer(0),type)); handle.Del(entity);
            for(int i=0;i<1000;i++) Cycle();
            long start=GC.GetAllocatedBytesForCurrentThread(); for(int i=0;i<1000;i++) Cycle();
            Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-start);
            void Cycle() { commands.Begin("owner"); commands.Add(handle,entity,snapshot); commands.Del(handle,entity); commands.Complete(); commands.End(); }
        }
        finally { scope.Exit(); preparer.Dispose(); world.Destroy(); }
    }
}
