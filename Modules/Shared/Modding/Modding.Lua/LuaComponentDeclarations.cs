using Karpik.Engine.Shared.Modding.Bindings;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

internal readonly record struct LuaWireField(string Name, SchemaScalarKind Kind);
internal sealed record LuaWireComponent(string Id, int Version, LuaWireField[] Fields);

internal static class LuaComponentDeclarations
{
    private const int MaxWireFieldsPerComponent = 32;

    internal static ComponentSchema[] Read(string modId, Table[] entries, int sharedEntryCount,
        out LuaWireComponent[] wire, out Dictionary<string, ComponentMigration>? migrations, out int sharedSchemaCount)
    {
        var schemas = new List<ComponentSchema>();
        sharedSchemaCount = 0;
        var projections = new List<LuaWireComponent>();
        migrations = null;
        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            DynValue components = entries[entryIndex].Get("components");
            if (components.IsNil()) continue;
            if (components.Type != DataType.Table) throw new InvalidDataException("components must be a table.");
            foreach (TablePair pair in components.Table.Pairs)
            {
                string local = LuaSystemDeclarations.ReadString(pair.Key, "component name");
                if (local.Contains(':') || local.Any(char.IsWhiteSpace)) throw new InvalidDataException($"Invalid local component name '{local}'.");
                string id = modId + ":" + local;
                if (pair.Value.Type != DataType.Table) throw new InvalidDataException($"Component '{id}' schema must be a table.");
                foreach (TablePair option in pair.Value.Table.Pairs)
                    if (option.Key.Type != DataType.String || option.Key.String is not ("version" or "fields" or "wire" or "migrate"))
                        throw new InvalidDataException($"Component '{id}': unknown schema option '{option.Key}'.");
                DynValue version = pair.Value.Table.Get("version"), fields = pair.Value.Table.Get("fields");
                if (version.Type != DataType.Number || !double.IsFinite(version.Number) || version.Number != Math.Truncate(version.Number) || version.Number < 1 || version.Number > int.MaxValue)
                    throw new InvalidDataException($"Component '{id}' version must be a positive int.");
                if (fields.Type != DataType.Table) throw new InvalidDataException($"Component '{id}' fields must be a table.");
                SchemaField[] description = LuaSchemaDeclarations.ReadFields(fields.Table, $"Component '{id}'");
                schemas.Add(new(id, (int)version.Number, description));
                if (entryIndex < sharedEntryCount) sharedSchemaCount++;
                DynValue migration = pair.Value.Table.Get("migrate");
                if (!migration.IsNil() && !(migrations ??= new(StringComparer.Ordinal)).TryAdd(id, ReadMigration(id, description, migration)))
                    throw new InvalidDataException($"Component '{id}': duplicate migration declaration.");
                DynValue selection = pair.Value.Table.Get("wire");
                if (selection.IsNil()) continue;
                if (entryIndex >= sharedEntryCount)
                    throw new InvalidDataException($"Component '{id}': a wire projection must be declared in Shared/main.lua.");
                projections.Add(new(id, (int)version.Number, ReadWireFields(id, description, selection)));
            }
        }
        wire = projections.OrderBy(static component => component.Id, StringComparer.Ordinal).ToArray();
        return schemas.ToArray();
    }

    private static ComponentMigration ReadMigration(string id, SchemaField[] fields, DynValue migration)
    {
        Table table = ReadOptions(migration, $"Component '{id}' migrate", "from", "fields");
        DynValue from = table.Get("from"), rules = table.Get("fields");
        if (from.Type != DataType.Number || !ExactInteger(from.Number, 1, int.MaxValue))
            throw new InvalidDataException($"Component '{id}': migration 'from' must be a positive version.");
        if (rules.Type != DataType.Table) throw new InvalidDataException($"Component '{id}': migration fields must be a table.");
        var result = new Dictionary<string, ComponentMigrationRule>(StringComparer.Ordinal);
        var destinations = fields.ToDictionary(static field => field.Name, StringComparer.Ordinal);
        foreach (TablePair pair in rules.Table.Pairs)
        {
            string target = LuaSystemDeclarations.ReadString(pair.Key, $"Component '{id}' migration field name");
            string location = $"Component '{id}' migration field '{target}'";
            if (!destinations.TryGetValue(target, out SchemaField field)) throw new InvalidDataException($"{location}: unknown destination field.");
            Table rule = ReadOptions(pair.Value, location, "from", "convert", "default");
            DynValue source = rule.Get("from"), convert = rule.Get("convert"), value = rule.Get("default");
            if (source.IsNil() == value.IsNil() || (!convert.IsNil() && source.IsNil()))
                throw new InvalidDataException($"{location}: specify either 'from' (with optional 'convert') or 'default'.");
            bool widen = !convert.IsNil();
            if (widen && (convert.Type != DataType.String || convert.String != "i32_to_i64"))
                throw new InvalidDataException($"{location}: only 'i32_to_i64' conversion is supported.");
            if (!result.TryAdd(target, source.IsNil() ? new(null, false, ReadDefault(field, value, location)) :
                    new(LuaSystemDeclarations.ReadString(source, $"{location} source"), widen, null)))
                throw new InvalidDataException($"{location}: duplicate rule.");
        }
        return new((int)from.Number, result);
    }

    private static Table ReadOptions(DynValue input, string location, params string[] names)
    {
        if (input.Type != DataType.Table) throw new InvalidDataException($"{location}: expected a table.");
        foreach (TablePair pair in input.Table.Pairs)
            if (pair.Key.Type != DataType.String || !names.Contains(pair.Key.String, StringComparer.Ordinal))
                throw new InvalidDataException($"{location}: unknown option '{pair.Key}'.");
        return input.Table;
    }

    private static bool ExactInteger(double value, double min, double max) =>
        value >= min && value <= max && value == Math.Truncate(value);

    private static object ReadDefault(SchemaField field, DynValue value, string location)
    {
        if (field.Value is null)
        {
            if (field.Kind == SchemaScalarKind.Bool && value.Type == DataType.Boolean) return value.Boolean;
            if (value.Type == DataType.Number && double.IsFinite(value.Number))
            {
                const double safeInteger = 9_007_199_254_740_991; // Lua numbers cannot distinguish every i64/u64 literal.
                double n = value.Number;
                object? exact = field.Kind switch
                {
                    SchemaScalarKind.I8 when ExactInteger(n, sbyte.MinValue, sbyte.MaxValue) => (object)(sbyte)n,
                    SchemaScalarKind.U8 when ExactInteger(n, byte.MinValue, byte.MaxValue) => (object)(byte)n,
                    SchemaScalarKind.I16 when ExactInteger(n, short.MinValue, short.MaxValue) => (object)(short)n,
                    SchemaScalarKind.U16 when ExactInteger(n, ushort.MinValue, ushort.MaxValue) => (object)(ushort)n,
                    SchemaScalarKind.I32 when ExactInteger(n, int.MinValue, int.MaxValue) => (object)(int)n,
                    SchemaScalarKind.U32 when ExactInteger(n, uint.MinValue, uint.MaxValue) => (object)(uint)n,
                    SchemaScalarKind.I64 when ExactInteger(n, -safeInteger, safeInteger) => (object)(long)n,
                    SchemaScalarKind.U64 when ExactInteger(n, 0, safeInteger) => (object)(ulong)n,
                    SchemaScalarKind.F32 when (double)(float)n == n => (object)(float)n,
                    SchemaScalarKind.F64 => n,
                    _ => null
                };
                if (exact is not null) return exact;
            }
        }
        throw new InvalidDataException($"{location}: 'default' is not a supported exact value for the destination type.");
    }

    private static LuaWireField[] ReadWireFields(string id, SchemaField[] fields, DynValue selection)
    {
        if (selection.Type != DataType.Table)
            throw new InvalidDataException($"Component '{id}': wire must be a dense field-name array.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<LuaWireField>();
        int highest = 0;
        foreach (TablePair pair in selection.Table.Pairs)
        {
            if (pair.Key.Type != DataType.Number || pair.Key.Number < 1 || pair.Key.Number > MaxWireFieldsPerComponent ||
                pair.Key.Number != Math.Truncate(pair.Key.Number))
                throw new InvalidDataException($"Component '{id}': wire must be a dense array of at most {MaxWireFieldsPerComponent} fields.");
            highest = Math.Max(highest, (int)pair.Key.Number);
            string fieldName = LuaSystemDeclarations.ReadString(pair.Value, $"Component '{id}' wire field name");
            if (!names.Add(fieldName)) throw new InvalidDataException($"Component '{id}': duplicate wire field '{fieldName}'.");
            int index = Array.FindIndex(fields, field => field.Name == fieldName);
            if (index < 0) throw new InvalidDataException($"Component '{id}': unknown field '{fieldName}' in wire projection.");
            var field = fields[index];
            if (field.Value is not null || WireSize(field.Kind) == 0)
                throw new InvalidDataException($"Component '{id}': unsupported wire field '{fieldName}'.");
            result.Add(new(fieldName, field.Kind));
        }
        if (result.Count == 0 || highest != result.Count)
            throw new InvalidDataException($"Component '{id}': wire must be a nonempty dense field-name array; omit it to opt out.");
        return result.OrderBy(static field => field.Name, StringComparer.Ordinal).ToArray();
    }

    internal static int WireSize(SchemaScalarKind kind) => kind switch
    {
        SchemaScalarKind.Bool or SchemaScalarKind.U8 => 1,
        SchemaScalarKind.U16 => 2,
        SchemaScalarKind.I32 or SchemaScalarKind.F32 => 4,
        SchemaScalarKind.I64 or SchemaScalarKind.F64 => 8,
        _ => 0,
    };

    internal static bool SameWire(ReadOnlySpan<LuaWireComponent> previous, ReadOnlySpan<LuaWireComponent> next)
    {
        if (previous.Length != next.Length) return false;
        for (int i = 0; i < previous.Length; i++)
        {
            var a = previous[i];
            var b = next[i];
            if (a.Id != b.Id || a.Version != b.Version || !a.Fields.AsSpan().SequenceEqual(b.Fields)) return false;
        }
        return true;
    }
}
