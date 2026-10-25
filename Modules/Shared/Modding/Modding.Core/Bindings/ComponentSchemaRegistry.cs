using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding.Bindings;

public enum SchemaScalarKind { F32 }
public readonly record struct SchemaField(string Name, SchemaScalarKind Kind);
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
            if (string.IsNullOrWhiteSpace(schema.Id) || schema.Version < 1 || schema.Fields.Length == 0)
                throw new InvalidDataException("A schema requires an ID, positive version and supported f32 fields.");
            SchemaField[] fields = schema.Fields.OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < fields.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(fields[i].Name) || fields[i].Kind != SchemaScalarKind.F32 ||
                    (i > 0 && fields[i - 1].Name == fields[i].Name))
                    throw new InvalidDataException($"Invalid fields in schema '{schema.Id}'.");
            }
            var canonical = new ComponentSchema(schema.Id, schema.Version, fields);
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
        return Array.ConvertAll(schemas, schema => new ComponentSchema(schema.Id, schema.Version, (SchemaField[])schema.Fields.Clone()));
    }

    private static bool Same(ComponentSchema a, ComponentSchema b) => a.Id == b.Id && a.Version == b.Version && a.Fields.AsSpan().SequenceEqual(b.Fields);
}
