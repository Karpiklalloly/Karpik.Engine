using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding.Bindings;

public enum SchemaScalarKind { F32 = 0, Bool = 1, I32 = 2, U32 = 3, F64 = 4, I8 = 5, U8 = 6, I16 = 7, U16 = 8, Char = 9, I64 = 10, U64 = 11, Decimal = 12, NInt = 13, NUInt = 14, Pointer = 15 }
public readonly record struct SchemaField(string Name, SchemaScalarKind Kind)
{
    public SchemaValue? Value { get; init; }
    public SchemaField(string Name, SchemaValue value) : this(Name, value.ScalarKind) => Value = value;
}
public sealed record ComponentSchema(string Id, int Version, params SchemaField[] Fields);

internal sealed record ComponentMigration(int FromVersion, Dictionary<string, ComponentMigrationRule> Fields);
internal readonly record struct ComponentMigrationRule(string? Source, bool WidenI32, object? DefaultValue);
internal readonly record struct ComponentFieldTransfer(string Target, ComponentMigrationRule Rule);

[Export(typeof(ComponentSchemaRegistry))]
[ServiceRegistration(ModuleScope.ModSet, ServiceLifetime.Singleton)]
public sealed class ComponentSchemaRegistry
{
    private readonly object _freezeLock = new();
    private volatile ComponentSchema[]? _schemas;
    public bool IsFrozen => _schemas is not null;

    internal static ComponentSchema[] Canonicalize(ReadOnlySpan<ComponentSchema> schemas)
    {
        var unique = new Dictionary<string, ComponentSchema>(StringComparer.Ordinal);
        foreach (ComponentSchema schema in schemas)
        {
            ComponentSchema canonical = SchemaTree.Canonicalize(schema);
            if (unique.TryGetValue(schema.Id, out var existing) && !Same(existing, canonical))
                throw new InvalidDataException($"Conflicting schema '{schema.Id}'.");
            unique[schema.Id] = canonical;
        }
        return unique.Values.OrderBy(schema => schema.Id, StringComparer.Ordinal).ToArray();
    }

    public void ValidateAndFreeze(ReadOnlySpan<ComponentSchema> schemas)
    {
        ComponentSchema[] candidate = Canonicalize(schemas);
        lock (_freezeLock)
        {
            if (_schemas is null) { _schemas = candidate; return; }
            if (_schemas.Length != candidate.Length) throw new InvalidDataException("ModSet component schema set changed.");
            for (int i = 0; i < candidate.Length; i++)
                if (!Same(_schemas[i], candidate[i])) throw new InvalidDataException($"ModSet schema '{candidate[i].Id}' changed.");
        }
    }

    public ComponentSchema[] GetSchemas()
    {
        ComponentSchema[] schemas = _schemas ?? throw new InvalidOperationException("Component schemas are not frozen.");
        return Array.ConvertAll(schemas, SchemaTree.Canonicalize);
    }

    internal static void RequireSameForSurvivor(string modId, ReadOnlySpan<ComponentSchema> previous, ReadOnlySpan<ComponentSchema> next)
    {
        ComponentSchema[] before = Canonicalize(previous);
        ComponentSchema[] after = Canonicalize(next);
        if (before.Length == after.Length)
        {
            int index = 0;
            while (index < before.Length && Same(before[index], after[index])) index++;
            if (index == before.Length) return;
        }
        throw new InvalidDataException($"Live selection cannot change the component schemas of surviving mod '{modId}'.");
    }

    internal static ComponentFieldTransfer[] PlanMigration(ComponentSchema before, ComponentSchema after,
        ComponentMigration? declaration, out string? droppedField)
    {
        if (declaration is null || declaration.FromVersion != before.Version || after.Version <= before.Version)
            throw new InvalidDataException($"Schema '{before.Id}': migration must specify the accepted version {before.Version} and a strictly newer destination version.");

        var sources = before.Fields.ToDictionary(static field => field.Name, StringComparer.Ordinal);
        var destinations = after.Fields.ToDictionary(static field => field.Name, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var transfers = new List<ComponentFieldTransfer>(destinations.Count);
        foreach (var (name, rule) in declaration.Fields)
        {
            if (!destinations.TryGetValue(name, out var target)) throw Error(name, "unknown destination field.");
            if (rule.Source is { } source)
            {
                if (!sources.TryGetValue(source, out var old)) throw Error(name, $"unknown source '{source}'.");
                if (rule.WidenI32)
                {
                    if (old.Value is not null || target.Value is not null || old.Kind != SchemaScalarKind.I32 || target.Kind != SchemaScalarKind.I64)
                        throw Error(name, $"'i32_to_i64' requires an i32 source '{source}' and an i64 destination.");
                }
                else if (old.Value is not null || target.Value is not null || old.Kind != target.Kind)
                    throw Error(name, $"source '{source}' must be a matching scalar, or use an explicit supported conversion.");
                if (!used.Add(source)) throw Error(name, $"duplicate rule for source '{source}'.");
            }
            else if (sources.ContainsKey(name)) throw Error(name, "a default is only allowed for a new field.");
            transfers.Add(new(name, rule));
        }
        foreach (var target in after.Fields)
        {
            string name = target.Name;
            if (declaration.Fields.ContainsKey(name)) continue;
            if (!sources.TryGetValue(name, out var old) || !SchemaTree.SameFields([old], [target]))
                throw Error(name, "a changed or new field requires an explicit migration rule.");
            if (!used.Add(name)) throw Error(name, "the implicit copy collides with an explicit source rule.");
            transfers.Add(new(name, new(name, false, null)));
        }
        droppedField = before.Fields.FirstOrDefault(field => !used.Contains(field.Name)).Name;
        return transfers.ToArray();

        InvalidDataException Error(string field, string message) => new($"Schema '{before.Id}', migration field '{field}': {message}");
    }

    private static bool Same(ComponentSchema a, ComponentSchema b) => SchemaTree.Same(a, b);
}
