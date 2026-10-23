using Karpik.Engine.Shared.ECS.Scheduling;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

internal readonly record struct LuaSystemDeclaration(
    string Id, DynValue Callback, string[] Reads, string[] Writes, EcsSystemOrderDescriptor[] Orders);

internal static class LuaSystemDeclarations
{
    internal static LuaSystemDeclaration[] Read(string modId, Table[] entries, out bool declared)
    {
        declared = entries.Any(entry => !entry.Get("systems").IsNil());
        if (declared && entries.Any(entry => !entry.Get("on_update").IsNil()))
            throw new InvalidDataException("A mod cannot combine systems and on_update.");

        var systems = new List<LuaSystemDeclaration>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Table entry in entries)
        {
            foreach (DynValue value in ReadArray(entry.Get("systems"), "systems"))
            {
                if (value.Type != DataType.Table)
                    throw new InvalidDataException("Each systems entry must be a table.");

                Table table = value.Table;
                string localId = ReadString(table.Get("id"), "id");
                if (localId.Contains(':') || localId.Any(char.IsWhiteSpace))
                    throw new InvalidDataException($"Invalid local system id '{localId}'.");
                string id = $"{modId}:{localId}";
                try
                {
                    if (!ids.Add(id))
                        throw new InvalidDataException("Duplicate system id.");
                    foreach (TablePair pair in table.Pairs)
                    {
                        if (pair.Key.Type != DataType.String || pair.Key.String is not
                            ("id" or "callback" or "phase" or "reads" or "writes" or "before" or "after"))
                            throw new InvalidDataException($"Unknown system field '{pair.Key}'.");
                    }
                    if (ReadString(table.Get("phase"), "phase") != "update")
                        throw new InvalidDataException("Only phase = 'update' is supported.");
                    DynValue callback = table.Get("callback");
                    if (callback.Type != DataType.Function)
                        throw new InvalidDataException("callback must be a Lua function.");

                    var orders = new List<EcsSystemOrderDescriptor>();
                    AddOrders("before", EcsOrderKind.Before);
                    AddOrders("after", EcsOrderKind.After);
                    systems.Add(new(id, callback, ReadNames(table, "reads"), ReadNames(table, "writes"), orders.ToArray()));

                    void AddOrders(string field, EcsOrderKind kind)
                    {
                        foreach (string target in ReadNames(table, field))
                        {
                            int separator = target.IndexOf(':');
                            if (separator <= 0 || separator == target.Length - 1 || target.Any(char.IsWhiteSpace))
                                throw new InvalidDataException($"{field} requires full system IDs, got '{target}'.");
                            if (target == id)
                                throw new InvalidDataException($"{field} cannot target the same system '{id}'.");
                            orders.Add(new(target, kind));
                        }
                    }
                }
                catch (InvalidDataException error)
                {
                    throw new InvalidDataException($"System '{id}': {error.Message}", error);
                }
            }
        }
        return systems.ToArray();
    }

    private static string[] ReadNames(Table table, string field) =>
        ReadArray(table.Get(field), field).Select(value => ReadString(value, field)).ToArray();

    private static string ReadString(DynValue value, string field)
    {
        if (value.Type != DataType.String || string.IsNullOrWhiteSpace(value.String))
            throw new InvalidDataException($"{field} must be a nonempty string.");
        return value.String;
    }

    private static DynValue[] ReadArray(DynValue value, string field)
    {
        if (value.IsNil()) return [];
        if (value.Type != DataType.Table)
            throw new InvalidDataException($"{field} must be an array.");

        TablePair[] pairs = value.Table.Pairs.ToArray();
        var result = new DynValue[pairs.Length];
        foreach (TablePair pair in pairs)
        {
            double key = pair.Key.Number;
            if (pair.Key.Type != DataType.Number || !double.IsFinite(key) || key != Math.Truncate(key) ||
                key < 1 || key > pairs.Length)
                throw new InvalidDataException($"{field} must be a dense array starting at 1.");
            result[(int)key - 1] = pair.Value;
        }
        return result;
    }
}
