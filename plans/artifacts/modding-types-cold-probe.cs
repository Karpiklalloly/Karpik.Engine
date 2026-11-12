using System.Diagnostics;
using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using MoonSharp.Interpreter;

var schema = new ComponentSchema("probe:Shape",1,
    new SchemaField("Position",SchemaValue.Struct(new("X",SchemaScalarKind.F32),new("Y",SchemaScalarKind.F64))),
    new SchemaField("Points",SchemaValue.Fixed(SchemaValue.Struct(new("X",SchemaScalarKind.I32),new("Y",SchemaScalarKind.F64)),2)),
    new SchemaField("Wide",SchemaScalarKind.I64));
long preparationStart = GC.GetAllocatedBytesForCurrentThread();
var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze([schema]);
var world = new EcsDefaultWorld(); using var scope = new ComponentCallbackScope();
using var preparer = new ComponentQueryPreparer(world,new ClrComponentBindings([]),schemas,scope);
preparer.Initialize();
var handle=preparer.Prepare(new("source",[],[],[new("probe:Shape",false)]),"probe").Components["probe:Shape"];
int entity=world.NewEntity();scope.Enter("probe");handle.Add(entity);
Console.WriteLine($"Common schema/world/query preparation: {GC.GetAllocatedBytesForCurrentThread()-preparationStart} B (cold; includes world).");
const int count=256;
var retained=new ComponentSnapshot[count];
for(int i=0;i<32;i++) { _=handle.NewSnapshot();_=handle.Copy(entity); }
long heapBefore=GC.GetTotalMemory(true), before=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<count;i++) retained[i]=handle.NewSnapshot();
Console.WriteLine($"Common new_snapshot L=7: {(GC.GetAllocatedBytesForCurrentThread()-before)/(double)count:F0} B/call.");
Console.WriteLine($"Retained {count} snapshots managed-live delta: {GC.GetTotalMemory(true)-heapBefore} B.");
before=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<count;i++) retained[i]=handle.Copy(entity);
Console.WriteLine($"Common copy L=7: {(GC.GetAllocatedBytesForCurrentThread()-before)/(double)count:F0} B/call.");
GC.KeepAlive(retained);scope.Exit();preparer.Dispose();world.Destroy();
var script=new Script();
var register=typeof(LuaScriptRuntime).Assembly.GetType("Karpik.Engine.Shared.Modding.Lua.LuaExactValues")!.GetMethod("Register",BindingFlags.NonPublic|BindingFlags.Static)!.CreateDelegate<Action<Script>>();
register(script);
foreach(var item in new[]{("empty","return function() return 1 end"),("i64 string constructor","return function() return types.i64('9007199254740993') end"),("decimal string constructor","return function() return types.decimal('0.1234567890123456789012345678') end"),("decimal tostring","local x=types.decimal('0.1234567890123456789012345678'); return function() return tostring(x) end")})
{
 var fn=script.DoString(item.Item2);for(int i=0;i<128;i++)script.Call(fn);
 before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<256;i++)script.Call(fn);
 Console.WriteLine($"Lua {item.Item1}: {(GC.GetAllocatedBytesForCurrentThread()-before)/256d:F0} B/call (includes Script.Call).");
}
Console.WriteLine($"Probe process observed peak working set: {Process.GetCurrentProcess().PeakWorkingSet64} B; includes CLR/code/preparation, not a VM-only peak.");
