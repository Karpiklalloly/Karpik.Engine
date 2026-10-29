using Karpik.Engine.Shared.Modding.Bindings;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

internal static class LuaComponentDeclarations
{
    internal static ComponentSchema[] Read(string modId, Table[] entries)
    {
        var schemas = new List<ComponentSchema>();
        foreach (Table entry in entries)
        {
            DynValue components = entry.Get("components");
            if (components.IsNil()) continue;
            if (components.Type != DataType.Table) throw new InvalidDataException("components must be a table.");
            foreach (TablePair pair in components.Table.Pairs)
            {
                string local = LuaSystemDeclarations.ReadString(pair.Key, "component name");
                if (local.Contains(':') || local.Any(char.IsWhiteSpace)) throw new InvalidDataException($"Invalid local component name '{local}'.");
                string id = modId + ":" + local;
                if (pair.Value.Type != DataType.Table) throw new InvalidDataException($"Component '{id}' schema must be a table.");
                foreach (TablePair option in pair.Value.Table.Pairs)
                    if (option.Key.Type != DataType.String || option.Key.String is not ("version" or "fields"))
                        throw new InvalidDataException($"Component '{id}': unknown schema option '{option.Key}'.");
                DynValue version = pair.Value.Table.Get("version"), fields = pair.Value.Table.Get("fields");
                if (version.Type != DataType.Number || !double.IsFinite(version.Number) || version.Number != Math.Truncate(version.Number) || version.Number < 1 || version.Number > int.MaxValue)
                    throw new InvalidDataException($"Component '{id}' version must be a positive int.");
                if (fields.Type != DataType.Table) throw new InvalidDataException($"Component '{id}' fields must be a table.");
                var description = new List<SchemaField>();
                foreach (TablePair field in fields.Table.Pairs)
                {
                    string name = LuaSystemDeclarations.ReadString(field.Key, "field name");
                    if (field.Value.Type != DataType.String)
                        throw new InvalidDataException($"Component '{id}', field '{name}': a scalar type name is required.");
                    SchemaScalarKind kind = field.Value.String switch
                    {
                        "bool" => SchemaScalarKind.Bool,
                        "i32" => SchemaScalarKind.I32,
                        "u32" => SchemaScalarKind.U32,
                        "f32" => SchemaScalarKind.F32,
                        "f64" => SchemaScalarKind.F64,
                        _ => throw new InvalidDataException($"Component '{id}', field '{name}': unsupported scalar type '{field.Value.String}'; expected bool, i32, u32, f32 or f64.")
                    };
                    description.Add(new(name, kind));
                }
                if (description.Count == 0) throw new InvalidDataException($"Component '{id}': empty schemas are not implemented in this slice.");
                schemas.Add(new(id, (int)version.Number, description.ToArray()));
            }
        }
        return schemas.ToArray();
    }
}
