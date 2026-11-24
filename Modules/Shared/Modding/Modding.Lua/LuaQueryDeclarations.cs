using Karpik.Engine.Shared.Modding.Bindings;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

internal static class LuaQueryDeclarations
{
    internal static ComponentQueryDefinition[] Read(DynValue value)
    {
        if (value.IsNil()) return [];
        if (value.Type != DataType.Table) throw new InvalidDataException("queries must be a table.");
        var result = new List<ComponentQueryDefinition>();
        foreach (TablePair pair in value.Table.Pairs)
        {
            string name = LuaSystemDeclarations.ReadString(pair.Key, "query name");
            try
            {
                if (pair.Value.Type != DataType.Table) throw new InvalidDataException("A query must be a table.");
                Table query = pair.Value.Table;
                foreach (TablePair field in query.Pairs)
                    if (field.Key.Type != DataType.String || field.Key.String is not ("inc" or "exc" or "opt"))
                        throw new InvalidDataException($"Unknown query field '{field.Key}'.");
                result.Add(new(name, ReadGroup(query.Get("inc")), ReadGroup(query.Get("exc")), ReadGroup(query.Get("opt"))));
            }
            catch (InvalidDataException error) { throw new InvalidDataException($"Query '{name}': {error.Message}", error); }
        }
        return result.OrderBy(query => query.Name, StringComparer.Ordinal).ToArray();
    }

    private static ComponentQueryEntry[] ReadGroup(DynValue value)
    {
        if (value.IsNil()) return [];
        if (value.Type != DataType.Table) throw new InvalidDataException("inc/exc/opt must be component tables.");
        var result = new List<ComponentQueryEntry>();
        foreach (TablePair pair in value.Table.Pairs)
        {
            string id = LuaSystemDeclarations.ReadString(pair.Key, "component ID");
            if (pair.Value.Type != DataType.Table) throw new InvalidDataException($"Component '{id}' access must be a table.");
            foreach (TablePair option in pair.Value.Table.Pairs)
                if (option.Key.Type != DataType.String || option.Key.String != "readonly")
                    throw new InvalidDataException($"Component '{id}': unknown access option '{option.Key}'.");
            DynValue readOnly = pair.Value.Table.Get("readonly");
            if (!readOnly.IsNil() && readOnly.Type != DataType.Boolean)
                throw new InvalidDataException($"Component '{id}': readonly must be boolean.");
            result.Add(new(id, readOnly.IsNil() || readOnly.Boolean));
        }
        return result.OrderBy(entry => entry.ComponentId, StringComparer.Ordinal).ToArray();
    }
}
