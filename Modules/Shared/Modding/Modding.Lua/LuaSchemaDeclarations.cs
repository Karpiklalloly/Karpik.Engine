using System.Text;
using Karpik.Engine.Shared.Modding.Bindings;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

/// <summary>Parses inline schema trees during preparation, without recursion through untrusted Lua tables.</summary>
internal static class LuaSchemaDeclarations
{
    internal static SchemaField[] ReadFields(Table fields, string location)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        var fieldResults = new Dictionary<Table, SchemaField[]>(ReferenceEqualityComparer.Instance);
        var valueResults = new Dictionary<Table, SchemaValue>(ReferenceEqualityComparer.Instance);
        var active = new HashSet<Table>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<WorkItem>();
        pending.Push(new WorkItem(fields, true, new Location(location)));
        while (pending.TryPop(out var item))
        {
            if (item.IsFields ? fieldResults.ContainsKey(item.Source) : valueResults.ContainsKey(item.Source)) continue;
            if (!item.Finish)
            {
                if (!active.Add(item.Source)) throw Error(item.Path, "cyclic schema tables are not allowed.");
                item.Finish = true;
                pending.Push(item);
                if (item.IsFields)
                {
                    var entries = new List<FieldEntry>();
                    foreach (TablePair pair in item.Source.Pairs)
                    {
                        string name = ReadString(pair.Key, item.Path, "a field name");
                        var path = new Location(item.Path, name);
                        if (pair.Value.Type is not (DataType.String or DataType.Table))
                            throw Error(path, "a scalar type token or inline type descriptor is required.");
                        entries.Add(new FieldEntry(name, pair.Value, path));
                        if (pair.Value.Type == DataType.Table) pending.Push(new WorkItem(pair.Value.Table, false, path));
                    }
                    if (entries.Count == 0) throw Error(item.Path, "empty fields are not implemented.");
                    item.Entries = entries.ToArray();
                }
                else
                {
                    item.Kind = ReadString(item.Source.Get("kind"), item.Path, "a descriptor kind");
                    switch (item.Kind)
                    {
                        case "struct":
                            ValidateOptions(item, "kind", "fields");
                            item.Child = ReadTable(item.Source.Get("fields"), item.Path, "struct fields");
                            pending.Push(new WorkItem(item.Child, true, item.Path));
                            break;
                        case "fixed":
                            ValidateOptions(item, "kind", "element", "length");
                            DynValue length = item.Source.Get("length");
                            if (length.Type != DataType.Number || !double.IsFinite(length.Number) || length.Number != Math.Truncate(length.Number) || length.Number < 1 || length.Number > int.MaxValue)
                                throw Error(item.Path, "fixed length must be a positive int.");
                            item.Length = (int)length.Number;
                            item.Element = item.Source.Get("element");
                            if (item.Element.Type == DataType.Table)
                                pending.Push(new WorkItem(item.Element.Table, false, new Location(item.Path, "element")));
                            else if (item.Element.Type != DataType.String)
                                throw Error(item.Path, "fixed element must be a scalar token or inline scalar/struct descriptor.");
                            break;
                        case "enum":
                            ValidateOptions(item, "kind", "underlying");
                            item.ScalarKind = ReadScalar(item.Source.Get("underlying"), item.Path);
                            if (item.ScalarKind is not (SchemaScalarKind.I8 or SchemaScalarKind.U8 or SchemaScalarKind.I16 or SchemaScalarKind.U16 or SchemaScalarKind.I32 or SchemaScalarKind.U32 or SchemaScalarKind.I64 or SchemaScalarKind.U64))
                                throw Error(item.Path, "enum underlying must be a fixed-width integer type.");
                            break;
                        case "pointer":
                            ValidateOptions(item, "kind", "type");
                            item.PointerType = ReadString(item.Source.Get("type"), item.Path, "a host pointer type ID");
                            for (int i = 0; i < item.PointerType.Length; i++)
                                if (char.IsWhiteSpace(item.PointerType[i])) throw Error(item.Path, "host pointer type ID cannot contain whitespace.");
                            break;
                        default: throw Error(item.Path, $"unknown descriptor kind '{item.Kind}'.");
                    }
                }
                continue;
            }

            if (item.IsFields)
            {
                var result = new SchemaField[item.Entries!.Length];
                for (int i = 0; i < result.Length; i++)
                {
                    var entry = item.Entries[i];
                    result[i] = entry.Value.Type == DataType.String
                        ? new SchemaField(entry.Name, ReadScalar(entry.Value, entry.Path))
                        : new SchemaField(entry.Name, valueResults[entry.Value.Table]);
                }
                fieldResults.Add(item.Source, result);
            }
            else
            {
                SchemaValue result;
                switch (item.Kind)
                {
                    case "struct": result = SchemaValue.Struct(fieldResults[item.Child!]); break;
                    case "fixed":
                        SchemaValue element = item.Element!.Type == DataType.String
                            ? SchemaValue.Scalar(ReadScalar(item.Element, new Location(item.Path, "element")))
                            : valueResults[item.Element.Table];
                        if (element.NodeKind == SchemaValueKind.Fixed)
                            throw Error(item.Path, "a fixed element must be scalar or struct; direct fixed-of-fixed declarations are not supported.");
                        result = SchemaValue.Fixed(element, item.Length);
                        break;
                    case "enum": result = SchemaValue.Enum(item.ScalarKind); break;
                    case "pointer": result = SchemaValue.Pointer(item.PointerType!); break;
                    default: throw Error(item.Path, "unknown descriptor kind.");
                }
                valueResults.Add(item.Source, result);
            }
            active.Remove(item.Source);
        }
        return fieldResults[fields];
    }

    private static SchemaScalarKind ReadScalar(DynValue value, Location path)
    {
        string token = ReadString(value, path, "a scalar type token");
        return token switch
        {
            "bool" => SchemaScalarKind.Bool,
            "i8" => SchemaScalarKind.I8,
            "u8" => SchemaScalarKind.U8,
            "i16" => SchemaScalarKind.I16,
            "u16" => SchemaScalarKind.U16,
            "i32" => SchemaScalarKind.I32,
            "u32" => SchemaScalarKind.U32,
            "i64" => SchemaScalarKind.I64,
            "u64" => SchemaScalarKind.U64,
            "f32" => SchemaScalarKind.F32,
            "f64" => SchemaScalarKind.F64,
            "char" => SchemaScalarKind.Char,
            "decimal" => SchemaScalarKind.Decimal,
            "nint" => SchemaScalarKind.NInt,
            "nuint" => SchemaScalarKind.NUInt,
            _ => throw Error(path, $"unsupported scalar type '{token}'.")
        };
    }

    private static string ReadString(DynValue value, Location path, string expected) =>
        value.Type == DataType.String && !string.IsNullOrWhiteSpace(value.String)
            ? value.String : throw Error(path, expected + " must be a nonempty string.");

    private static Table ReadTable(DynValue value, Location path, string expected) =>
        value.Type == DataType.Table ? value.Table : throw Error(path, expected + " must be a table.");

    private static void ValidateOptions(WorkItem item, params string[] allowed)
    {
        foreach (TablePair pair in item.Source.Pairs)
        {
            if (pair.Key.Type != DataType.String)
                throw Error(item.Path, "descriptor option keys must be strings.");
            bool found = false;
            for (int i = 0; i < allowed.Length; i++)
                if (allowed[i] == pair.Key.String) { found = true; break; }
            if (!found) throw Error(item.Path, $"unknown '{item.Kind}' option '{pair.Key.String}'.");
        }
    }

    private static InvalidDataException Error(Location path, string message) => new($"{path.Format()}: {message}");

    private readonly record struct FieldEntry(string Name, DynValue Value, Location Path);

    private sealed class WorkItem(Table source, bool isFields, Location path)
    {
        internal Table Source { get; } = source;
        internal bool IsFields { get; } = isFields;
        internal Location Path { get; } = path;
        internal bool Finish;
        internal FieldEntry[]? Entries;
        internal string? Kind;
        internal Table? Child;
        internal DynValue? Element;
        internal int Length;
        internal SchemaScalarKind ScalarKind;
        internal string? PointerType;
    }

    // Keep linked path segments during preparation; only format full paths on an error.
    private sealed class Location
    {
        private readonly Location? _parent;
        private readonly string _part;
        internal Location(string root) => _part = root;
        internal Location(Location parent, string part) { _parent = parent; _part = part; }
        internal string Format()
        {
            var parts = new Stack<string>();
            Location? current = this;
            while (current is not null) { parts.Push(current._part); current = current._parent; }
            var result = new StringBuilder(parts.Pop());
            if (parts.Count == 0) return result.ToString();
            result.Append(", field '");
            bool first = true;
            while (parts.TryPop(out string? part))
            {
                if (!first) result.Append('.');
                result.Append(part); first = false;
            }
            return result.Append('\'').ToString();
        }
    }
}
