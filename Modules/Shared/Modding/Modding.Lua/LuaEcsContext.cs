using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.ECS.Scheduling;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Karpik.Engine.Shared.Modding.Lua;

internal sealed class LuaEcsContext
{
    private readonly ComponentCallbackScope _scope;
    private readonly string _owner;
    internal DynValue Value { get; }
    internal PreparedComponentQuery[] Queries { get; }
    internal EcsComponentAccessDescriptor[] Accesses { get; }
    internal string QueryNames { get; }

    internal LuaEcsContext(Script script, string owner, ComponentQueryDefinition[] definitions, ComponentQueryPreparer preparer, ComponentCallbackScope scope)
    {
        _scope = scope; _owner = owner;
        QueryNames = string.Join(", ", definitions.Select(definition => definition.Name));
        Queries = Array.ConvertAll(definitions, definition => preparer.Prepare(definition, owner));
        Accesses = ComponentQueryPreparer.InferAccesses(Queries);
        var context = new Table(script); var queries = new Table(script);
        for (int i = 0; i < definitions.Length; i++)
            queries.Set(definitions[i].Name, UserData.Create(new LuaQuery(script, Queries[i], $"System '{owner}', query '{definitions[i].Name}'"), new QueryDescriptor()));
        context.Set("queries", DynValue.NewTable(queries)); Value = DynValue.NewTable(context);
    }

    internal void Enter() { _scope.Enter(_owner); foreach (var query in Queries) query.Reset(); }
    internal void Exit() { foreach (var query in Queries) query.Reset(); _scope.Exit(); }

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
        internal DynValue Get { get; }
        internal DynValue Add { get; }
        internal DynValue Del { get; }
        internal DynValue Has { get; }
        internal LuaPool(PreparedComponentHandle handle, string location)
        {
            var view = new LuaView(handle, location); DynValue value = UserData.Create(view, new ViewDescriptor());
            Get = DynValue.NewCallback((_, args) =>
            {
                try { view.Current = handle.Get(EntityId(args, location)); return value; }
                catch (InvalidOperationException error) { throw OperationError(location, error); }
            });
            Add = DynValue.NewCallback((_, args) =>
            {
                try { view.Current = handle.Add(EntityId(args, location)); return value; }
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
        }
    }

    private sealed class LuaView
    {
        internal readonly Dictionary<string, int> Fields = new(StringComparer.Ordinal);
        internal PreparedComponentView Current = null!;
        internal string Location { get; }
        internal LuaView(PreparedComponentHandle handle, string location)
        {
            Location = location;
            for (int i = 0; i < handle.Fields.Length; i++) Fields.Add(handle.Fields.Span[i].Name, i);
        }
        internal int Field(DynValue key) => key.Type == DataType.String && Fields.TryGetValue(key.String, out int field)
            ? field : throw new ScriptRuntimeException($"{Location}: unknown component field '{key}'.");
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
        public DynValue MetaIndex(Script script, object obj, string metaname) => null!;
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
            return index.String switch { "get" => pool.Get, "add" => pool.Add, "del" => pool.Del, "has" => pool.Has, _ => throw new ScriptRuntimeException("Unknown component pool operation.") };
        }
    }
    private sealed class ViewDescriptor : Descriptor<LuaView>
    {
        public override DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            var view = (LuaView)obj; int field = view.Field(index);
            try
            {
                return view.Current.GetFieldKind(field) switch
                {
                    SchemaScalarKind.Bool => DynValue.NewBoolean(view.Current.ReadBool(field)),
                    SchemaScalarKind.I32 => DynValue.NewNumber(view.Current.ReadI32(field)),
                    SchemaScalarKind.U32 => DynValue.NewNumber(view.Current.ReadU32(field)),
                    SchemaScalarKind.F32 => DynValue.NewNumber(view.Current.ReadF32(field)),
                    SchemaScalarKind.F64 => DynValue.NewNumber(view.Current.ReadF64(field)),
                    _ => throw new ScriptRuntimeException($"{view.Location}: unsupported field kind.")
                };
            }
            catch (InvalidOperationException error) { throw OperationError($"{view.Location}, field '{index.String}'", error); }
        }
        public override bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        {
            var view = (LuaView)obj; int field = view.Field(index);
            try
            {
                SchemaScalarKind kind = view.Current.GetFieldKind(field);
                if (kind == SchemaScalarKind.Bool)
                {
                    if (value.Type != DataType.Boolean) throw AssignmentError(view, index, kind, "assignment requires a boolean");
                    view.Current.WriteBool(field, value.Boolean); return true;
                }
                if (value.Type != DataType.Number) throw AssignmentError(view, index, kind, "assignment requires a number");
                double number = value.Number;
                switch (kind)
                {
                    case SchemaScalarKind.I32:
                        if (!double.IsFinite(number) || number != Math.Truncate(number) || number < int.MinValue || number > int.MaxValue)
                            throw AssignmentError(view, index, kind, "assignment requires a finite integer in [-2147483648, 2147483647]");
                        view.Current.WriteI32(field, checked((int)number)); break;
                    case SchemaScalarKind.U32:
                        if (!double.IsFinite(number) || number != Math.Truncate(number) || number < 0 || number > uint.MaxValue)
                            throw AssignmentError(view, index, kind, "assignment requires a finite integer in [0, 4294967295]");
                        view.Current.WriteU32(field, checked((uint)number)); break;
                    case SchemaScalarKind.F32:
                        float converted = (float)number;
                        if (double.IsFinite(number) && float.IsInfinity(converted))
                            throw AssignmentError(view, index, kind, "finite number overflows f32");
                        view.Current.WriteF32(field, converted); break;
                    case SchemaScalarKind.F64:
                        view.Current.WriteF64(field, number); break;
                    default:
                        throw AssignmentError(view, index, kind, "unsupported field kind");
                }
                return true;
            }
            catch (InvalidOperationException error) { throw OperationError($"{view.Location}, field '{index.String}'", error); }
        }

        private static ScriptRuntimeException AssignmentError(LuaView view, DynValue index, SchemaScalarKind kind, string message) =>
            new($"{view.Location}, field '{index.String}' ({kind}): {message}.");
    }
}
