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

[Export(typeof(ComponentSchemaRegistry))]
[ServiceRegistration(ModuleScope.ModSet, ServiceLifetime.Singleton)]
public sealed class ComponentSchemaRegistry
{
    private readonly object _freezeLock = new();
    private volatile ComponentSchema[]? _schemas;
    public bool IsFrozen => _schemas is not null;

    public void ValidateAndFreeze(ReadOnlySpan<ComponentSchema> schemas)
    {
        var unique = new Dictionary<string, ComponentSchema>(StringComparer.Ordinal);
        foreach (ComponentSchema schema in schemas)
        {
            ComponentSchema canonical = SchemaTree.Canonicalize(schema);
            if (unique.TryGetValue(schema.Id, out var existing) && !Same(existing, canonical))
                throw new InvalidDataException($"Conflicting schema '{schema.Id}'.");
            unique[schema.Id] = canonical;
        }
        ComponentSchema[] candidate = unique.Values.OrderBy(schema => schema.Id, StringComparer.Ordinal).ToArray();
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

    private static bool Same(ComponentSchema a, ComponentSchema b) => SchemaTree.Same(a, b);
}
