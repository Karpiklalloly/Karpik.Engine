using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.ECS.Scheduling;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Karpik.Engine.Shared.Modding.Lua;

internal sealed class LuaEcsContext
{
    private readonly ComponentCallbackScope _scope;
    private readonly string _owner;
    private readonly StructuralCommandBuffer _commands;
    internal DynValue Value { get; }
    internal PreparedComponentQuery[] Queries { get; }
    internal EcsComponentAccessDescriptor[] Accesses { get; }
    internal string QueryNames { get; }

    internal LuaEcsContext(Script script, string owner, ComponentQueryDefinition[] definitions, ComponentQueryPreparer preparer, ComponentCallbackScope scope, StructuralCommandOptions commandOptions)
    {
        _scope = scope; _owner = owner; _commands = preparer.GetCommands(commandOptions);
        QueryNames = string.Join(", ", definitions.Select(definition => definition.Name));
        Queries = Array.ConvertAll(definitions, definition => preparer.Prepare(definition, owner));
        Accesses = ComponentQueryPreparer.InferAccesses(Queries);
        var context = new Table(script); var queries = new Table(script);
        for (int i = 0; i < definitions.Length; i++)
            queries.Set(definitions[i].Name, UserData.Create(new LuaQuery(script, Queries[i], $"System '{owner}', query '{definitions[i].Name}'"), new QueryDescriptor()));
        context.Set("queries", DynValue.NewTable(queries));
        context.Set("commands", UserData.Create(new LuaCommands(_commands, scope, owner), CommandsDescriptor.Instance));
        Value = DynValue.NewTable(context);
    }

    internal void Enter()
    {
        _scope.Enter(_owner);
        try { _commands.Begin(_scope, _owner); foreach (var query in Queries) query.Reset(); }
        catch { _scope.Exit(); throw; }
    }
    internal void Complete() => _commands.Complete();
    internal void Exit()
    {
        try { _commands.End(); foreach (var query in Queries) query.Reset(); }
        finally { _scope.Exit(); }
    }

    private sealed class LuaQuery
    {
        internal PreparedComponentQuery Query { get; }
        internal DynValue Components { get; }
        internal DynValue Entities { get; }
        internal LuaQuery(Script script, PreparedComponentQuery query, string location)
        {
            Query = query;
            var components = new Table(script);
            foreach (var pair in query.Components)
                components.Set(pair.Key, UserData.Create(new LuaPool(pair.Value, location + $", component '{pair.Key}'"), new PoolDescriptor()));
            Components = DynValue.NewTable(components);
            var iterator = DynValue.NewCallback((_, _) => query.MoveNext(out int entity) ? DynValue.NewNumber(entity) : DynValue.Nil);
            Entities = DynValue.NewCallback((_, _) => { query.Capture(); return iterator; });
        }
    }

    private sealed class LuaPool
    {
        internal PreparedComponentHandle Handle { get; }
        internal DynValue Get { get; }
        internal DynValue Add { get; }
        internal DynValue Del { get; }
        internal DynValue Has { get; }
        internal DynValue Copy { get; }
        internal DynValue NewSnapshot { get; }
        internal DynValue CopyTo { get; }
        internal LuaPool(PreparedComponentHandle handle, string location)
        {
            Handle = handle;
            var root = new DataRoot(location);
            DynValue value = BuildView(root, handle.Fields.Span);
            Get = DynValue.NewCallback((_, args) =>
            {
                try { root.Current = handle.Get(EntityId(args, location)); return value; }
                catch (InvalidOperationException error) { throw OperationError(location, error); }
            });
            Add = DynValue.NewCallback((_, args) =>
            {
                try { root.Current = handle.Add(EntityId(args, location)); return value; }
                catch (InvalidOperationException error) { throw OperationError(location, error); }
            });
            Del = DynValue.NewCallback((_, args) =>
            {
                try { handle.Del(EntityId(args, location)); return DynValue.Nil; }
                catch (InvalidOperationException error) { throw OperationError(location, error); }
            });
            Has = DynValue.NewCallback((_, args) =>
            {
                try { return DynValue.NewBoolean(handle.Has(EntityId(args, location))); }
                catch (InvalidOperationException error) { throw OperationError(location, error); }
            });
            Copy = DynValue.NewCallback((_, args) =>
            {
                try { return Wrap(handle.Copy(EntityId(args, location)), location); }
                catch (InvalidOperationException error) { throw OperationError(location, error); }
            });
            NewSnapshot = DynValue.NewCallback((_, _) =>
            {
                try { return Wrap(handle.NewSnapshot(), location); }
                catch (InvalidOperationException error) { throw OperationError(location, error); }
            });
            CopyTo = DynValue.NewCallback((_, args) =>
            {
                try
                {
                    DynValue target = args[2];
                    if (target.Type != DataType.UserData || target.UserData.Object is not DataNode node ||
                        !ReferenceEquals(node, node.Root.Node) || node.Root.Current is not ComponentSnapshot snapshot)
                        throw new InvalidOperationException("copy_to requires a root snapshot buffer.");
                    handle.CopyTo(EntityId(args, location), snapshot);
                    return target;
                }
                catch (InvalidOperationException error) { throw OperationError(location, error); }
            });
        }
    }

    // Tokens are immutable: a retained Lua userdata must never be rebound to a later invocation.
    private sealed class LuaPending(PendingEntity value)
    {
        internal PendingEntity Value { get; } = value;
    }
    private sealed class PendingDescriptor : Descriptor<LuaPending>
    {
        internal static readonly PendingDescriptor Instance = new();
        public override DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing) =>
            throw new ScriptRuntimeException("Pending entity tokens have no public members.");
    }
    private sealed class LuaCommands
    {
        private readonly StructuralCommandBuffer _buffer;
        private readonly ComponentCallbackScope _scope;
        private readonly string _owner;
        internal DynValue Create { get; }
        internal DynValue Add { get; }
        internal DynValue Del { get; }
        internal LuaCommands(StructuralCommandBuffer buffer, ComponentCallbackScope scope, string owner)
        {
            _buffer = buffer; _scope = scope; _owner = owner;
            Create = DynValue.NewCallback((_, args) =>
            {
                try
                {
                    Require(args, 1, 1);
                    return UserData.Create(new LuaPending(buffer.Create()), PendingDescriptor.Instance);
                }
                catch (Exception error) when (IsCommandError(error)) { throw Reject(error); }
            });
            Add = DynValue.NewCallback((_, args) =>
            {
                try
                {
                    Require(args, 3, 4);
                    var pool = Pool(args[1]);
                    ComponentSnapshot? snapshot = null;
                    if (args.Count == 4 && !args[3].IsNil())
                    {
                        if (args[3].Type != DataType.UserData || args[3].UserData.Object is not DataNode node ||
                            !ReferenceEquals(node, node.Root.Node) || node.Root.Current is not ComponentSnapshot data)
                            throw new ScriptRuntimeException("Command payload requires a root component snapshot.");
                        snapshot = data;
                    }
                    if (args[2].Type == DataType.UserData && args[2].UserData.Object is LuaPending pending)
                        buffer.Add(pool, pending.Value, snapshot);
                    else buffer.Add(pool, Target(args[2]), snapshot);
                    return DynValue.Nil;
                }
                catch (Exception error) when (IsCommandError(error)) { throw Reject(error); }
            });
            Del = DynValue.NewCallback((_, args) =>
            {
                try
                {
                    Require(args, 3, 3);
                    var pool = Pool(args[1]);
                    if (args[2].Type == DataType.UserData && args[2].UserData.Object is LuaPending pending)
                        buffer.Del(pool, pending.Value);
                    else buffer.Del(pool, Target(args[2]));
                    return DynValue.Nil;
                }
                catch (Exception error) when (IsCommandError(error)) { throw Reject(error); }
            });
        }
        private void Require(CallbackArguments args, int min, int max)
        {
            _buffer.RequireOwner(_scope, _owner);
            if (args.Count < min || args.Count > max || args[0].Type != DataType.UserData ||
                !ReferenceEquals(args[0].UserData.Object, this))
                throw new ScriptRuntimeException("Command requires ':' syntax and the expected arguments.");
        }
        internal void ValidateOwner() => _buffer.RequireOwner(_scope, _owner);
        internal ScriptRuntimeException Reject(Exception error)
        {
            _buffer.Invalidate(_scope);
            return OperationError("System '" + _owner + "', commands", error);
        }
        private static bool IsCommandError(Exception error) =>
            error is InvalidOperationException or ArgumentException or OverflowException or ScriptRuntimeException;
        private static PreparedComponentHandle Pool(DynValue value) =>
            value.Type == DataType.UserData && value.UserData.Object is LuaPool pool ? pool.Handle :
                throw new ScriptRuntimeException("Command requires a prepared query component pool.");
        private static int Target(DynValue value)
        {
            if (value.Type != DataType.Number || !double.IsFinite(value.Number) ||
                value.Number != Math.Truncate(value.Number) || value.Number <= 0 || value.Number > int.MaxValue)
                throw new ScriptRuntimeException("Command target requires an int entity ID or a pending entity token.");
            return (int)value.Number;
        }
    }
    private sealed class CommandsDescriptor : Descriptor<LuaCommands>
    {
        internal static readonly CommandsDescriptor Instance = new();
        public override DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            var commands = (LuaCommands)obj;
            try
            {
                commands.ValidateOwner();
                return index.String switch
                {
                    "create" => commands.Create, "add" => commands.Add, "del" => commands.Del,
                    _ => throw new ScriptRuntimeException("Unknown structural command operation.")
                };
            }
            catch (Exception error) when (error is InvalidOperationException or ScriptRuntimeException)
            { throw commands.Reject(error); }
        }
        public override bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing) =>
            throw ((LuaCommands)obj).Reject(new ScriptRuntimeException("Structural command API is readonly."));
    }

    private sealed class DataRoot(string location)
    {
        internal IComponentDataView Current = null!;
        internal DataNode Node = null!;
        internal ComponentFieldDescriptor[] Metadata = [];
        internal string Location { get; } = location;
    }
    private readonly record struct Member(int Field, DataNode? Node, DynValue? Value);
    private sealed class DataNode(DataRoot root, bool indexed, string path = "")
    {
        internal DataRoot Root { get; } = root;
        internal bool Indexed { get; } = indexed;
        internal string Path { get; } = path;
        internal readonly Dictionary<string, Member> Named = new(StringComparer.Ordinal);
        internal readonly List<Member> Elements = new();

        internal Member Find(DynValue key)
        {
            if (!Indexed)
            {
                if (key.Type == DataType.String && Named.TryGetValue(key.String, out var member)) return member;
                throw new ScriptRuntimeException($"{Root.Location}: unknown component field '{Path}' / '{key}'.");
            }
            if (key.Type != DataType.Number || !double.IsFinite(key.Number) || key.Number != Math.Truncate(key.Number) || key.Number < 1 || key.Number > Elements.Count)
                throw new ScriptRuntimeException($"{Root.Location}: field '{Path}': fixed-buffer index must be an integer in [1, {Elements.Count}].");
            return Elements[(int)key.Number - 1];
        }
        internal Member Get(ComponentPathSegment part) => part.Name is { } name
            ? Named.GetValueOrDefault(name, new(-1, null, null))
            : part.Index <= Elements.Count ? Elements[part.Index - 1] : new(-1, null, null);
        internal void Set(ComponentPathSegment part, Member member)
        {
            if (part.Name is { } name) Named.Add(name, member);
            else
            {
                while (Elements.Count < part.Index) Elements.Add(new(-1, null, null));
                Elements[part.Index - 1] = member;
            }
        }
    }

    private static DynValue Wrap(ComponentSnapshot snapshot, string location) =>
        BuildView(new DataRoot(location) { Current = snapshot }, snapshot.Fields.Span);

    private static DynValue BuildView(DataRoot root, ReadOnlySpan<ComponentFieldDescriptor> fields)
    {
        root.Node = new(root, false);
        root.Metadata = fields.ToArray();
        DynValue result = UserData.Create(root.Node, ViewDescriptor.Instance);
        for (int i = 0; i < fields.Length; i++)
        {
            ReadOnlySpan<ComponentPathSegment> path = fields[i].Path.Span;
            if (path.IsEmpty)
            {
                root.Node.Set(new(fields[i].Name), new(i, null, null));
                continue;
            }
            DataNode node = root.Node;
            for (int p = 0; p < path.Length; p++)
            {
                if (p == path.Length - 1) { node.Set(path[p], new(i, null, null)); break; }
                Member member = node.Get(path[p]);
                if (member.Node is null)
                {
                    if (member.Field >= 0) throw new InvalidDataException("Component binding has conflicting field paths.");
                    var childPath = path[p].Name is { } name
                        ? (node.Path.Length == 0 ? name : node.Path + "." + name) : node.Path + "[" + path[p].Index + "]";
                    var child = new DataNode(root, path[p + 1].Name is null, childPath);
                    member = new(-1, child, UserData.Create(child, ViewDescriptor.Instance));
                    node.Set(path[p], member);
                }
                node = member.Node!;
            }
        }
        return result;
    }

    private static ScriptRuntimeException OperationError(string location, Exception error) => new($"{location}: {error.Message}");
    private static int EntityId(CallbackArguments args, string location)
    {
        DynValue value = args[1];
        if (value.Type != DataType.Number || !double.IsFinite(value.Number) || value.Number != Math.Truncate(value.Number) || value.Number < int.MinValue || value.Number > int.MaxValue)
            throw new ScriptRuntimeException($"{location}: component operation requires an int entity ID.");
        return (int)value.Number;
    }

    private abstract class Descriptor<T> : IUserDataDescriptor
    {
        public string Name => typeof(T).Name;
        public Type Type => typeof(T);
        public abstract DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing);
        public virtual bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing) => false;
        public string AsString(object obj) => Name;
        public virtual DynValue MetaIndex(Script script, object obj, string metaname) => null!;
        public bool IsTypeCompatible(Type type, object obj) => type == typeof(T);
    }
    private sealed class QueryDescriptor : Descriptor<LuaQuery>
    {
        public override DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            var query = (LuaQuery)obj; query.Query.ValidateAccess();
            return index.String switch { "components" => query.Components, "entities" => query.Entities, _ => throw new ScriptRuntimeException("Unknown query member.") };
        }
    }
    private sealed class PoolDescriptor : Descriptor<LuaPool>
    {
        public override DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            var pool = (LuaPool)obj;
            return index.String switch
            {
                "get" => pool.Get, "add" => pool.Add, "del" => pool.Del, "has" => pool.Has,
                "copy" => pool.Copy, "new_snapshot" => pool.NewSnapshot, "copy_to" => pool.CopyTo,
                _ => throw new ScriptRuntimeException("Unknown component pool operation.")
            };
        }
    }
    private sealed class ViewDescriptor : Descriptor<DataNode>
    {
        internal static readonly ViewDescriptor Instance = new();
        private static readonly DynValue Length = DynValue.NewCallback((_, args) =>
        {
            var node = (DataNode)args[0].UserData.Object;
            try
            {
                node.Root.Current.ValidateAccess();
                if (!node.Indexed) throw new ScriptRuntimeException("Length requires a fixed buffer.");
                return DynValue.NewNumber(node.Elements.Count);
            }
            catch (InvalidOperationException error) { throw OperationError(node.Root.Location + ", field '" + node.Path + "'", error); }
        });
        public override DynValue MetaIndex(Script script, object obj, string metaname) => metaname == "__len" ? Length : null!;
        public override DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            var node = (DataNode)obj;
            Member member = node.Find(index);
            try
            {
                if (member.Node is not null) { node.Root.Current.ValidateAccess(); return member.Value!; }
                int field = member.Field;
                var current = node.Root.Current;
                return current.GetFieldKind(field) switch
                {
                    SchemaScalarKind.Bool => DynValue.NewBoolean(current.Read<bool>(field)),
                    SchemaScalarKind.I8 => DynValue.NewNumber(current.Read<sbyte>(field)),
                    SchemaScalarKind.U8 => DynValue.NewNumber(current.Read<byte>(field)),
                    SchemaScalarKind.I16 => DynValue.NewNumber(current.Read<short>(field)),
                    SchemaScalarKind.U16 => DynValue.NewNumber(current.Read<ushort>(field)),
                    SchemaScalarKind.Char => DynValue.NewNumber(current.Read<char>(field)),
                    SchemaScalarKind.I32 => DynValue.NewNumber(current.Read<int>(field)),
                    SchemaScalarKind.U32 => DynValue.NewNumber(current.Read<uint>(field)),
                    SchemaScalarKind.F32 => DynValue.NewNumber(current.Read<float>(field)),
                    SchemaScalarKind.F64 => DynValue.NewNumber(current.Read<double>(field)),
                    SchemaScalarKind.I64 => LuaExactValues.Create(current.Read<long>(field)),
                    SchemaScalarKind.U64 => LuaExactValues.Create(current.Read<ulong>(field)),
                    SchemaScalarKind.Decimal => LuaExactValues.Create(current.Read<decimal>(field)),
                    SchemaScalarKind.NInt => LuaExactValues.Create((long)current.Read<nint>(field)),
                    SchemaScalarKind.NUInt => LuaExactValues.Create((ulong)current.Read<nuint>(field)),
                    SchemaScalarKind.Pointer => LuaPointerValues.Create(current.Read<ComponentPointerValue>(field), current.Fields.Span[field].PointerRegistry!),
                    _ => throw new ScriptRuntimeException("Unsupported field kind.")
                };
            }
            catch (Exception error) when (error is InvalidOperationException or ScriptRuntimeException or OverflowException)
            { throw FieldError(node, member, error); }
        }
        public override bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        {
            var node = (DataNode)obj; Member member = node.Find(index);
            try
            {
                if (member.Node is not null) throw new ScriptRuntimeException("Assign scalar fields or fixed elements individually.");
                int field = member.Field;
                var current = node.Root.Current;
                SchemaScalarKind kind = current.GetFieldKind(field);
                switch (kind)
                {
                    case SchemaScalarKind.Bool:
                        if (value.Type != DataType.Boolean) throw new ScriptRuntimeException("Bool assignment requires a boolean.");
                        current.Write(field, value.Boolean); return true;
                    case SchemaScalarKind.I64: current.Write(field, LuaExactValues.ReadI64(value)); return true;
                    case SchemaScalarKind.U64: current.Write(field, LuaExactValues.ReadU64(value)); return true;
                    case SchemaScalarKind.Decimal: current.Write(field, LuaExactValues.ReadDecimal(value)); return true;
                    case SchemaScalarKind.NInt: current.Write(field, checked((nint)LuaExactValues.ReadI64(value))); return true;
                    case SchemaScalarKind.NUInt: current.Write(field, checked((nuint)LuaExactValues.ReadU64(value))); return true;
                    case SchemaScalarKind.Pointer:
                        var descriptor = current.Fields.Span[field];
                        current.Write(field, LuaPointerValues.Read(value, descriptor.PointerRegistry!, descriptor.PointerType)); return true;
                }
                if (value.Type != DataType.Number) throw new ScriptRuntimeException($"{kind} assignment requires a number.");
                double number = value.Number;
                switch (kind)
                {
                    case SchemaScalarKind.I8: RequireInteger(kind, number, sbyte.MinValue, sbyte.MaxValue); current.Write(field, (sbyte)number); break;
                    case SchemaScalarKind.U8: RequireInteger(kind, number, 0, byte.MaxValue); current.Write(field, (byte)number); break;
                    case SchemaScalarKind.I16: RequireInteger(kind, number, short.MinValue, short.MaxValue); current.Write(field, (short)number); break;
                    case SchemaScalarKind.U16: RequireInteger(kind, number, 0, ushort.MaxValue); current.Write(field, (ushort)number); break;
                    case SchemaScalarKind.Char: RequireInteger(kind, number, 0, char.MaxValue); current.Write(field, (char)number); break;
                    case SchemaScalarKind.I32: RequireInteger(kind, number, int.MinValue, int.MaxValue); current.Write(field, (int)number); break;
                    case SchemaScalarKind.U32: RequireInteger(kind, number, 0, uint.MaxValue); current.Write(field, (uint)number); break;
                    case SchemaScalarKind.F32:
                        float converted = (float)number;
                        if (double.IsFinite(number) && float.IsInfinity(converted)) throw new ScriptRuntimeException("Finite number overflows f32.");
                        current.Write(field, converted); break;
                    case SchemaScalarKind.F64: current.Write(field, number); break;
                    default: throw new ScriptRuntimeException("Unsupported field kind.");
                }
                return true;
            }
            catch (Exception error) when (error is InvalidOperationException or ScriptRuntimeException or OverflowException)
            { throw FieldError(node, member, error); }
        }
        private static ScriptRuntimeException FieldError(DataNode node, Member member, Exception error)
        {
            string field = member.Field >= 0 ? node.Root.Metadata[member.Field].Name : node.Path;
            string expected = member.Field >= 0 ? node.Root.Metadata[member.Field].Kind.ToString() : "composite";
            return OperationError(node.Root.Location + ", field '" + field + "' (" + expected + ")", error);
        }
        private static void RequireInteger(SchemaScalarKind kind, double number, double min, double max)
        {
            if (!double.IsFinite(number) || number != Math.Truncate(number) || number < min || number > max)
                throw new ScriptRuntimeException($"{kind} assignment requires a finite integer in [{min}, {max}].");
        }
    }
}
