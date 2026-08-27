using System.Text;
using System.Text.Json;

namespace Karpik.Content.Core;

public static class CanonicalJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    public static string SerializeCanonical(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteCanonical(writer, element);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string SerializeMetaCanonical(AssetMeta meta)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("assetId", meta.AssetId.ToCanonicalString());
            writer.WriteString("declaredType", meta.DeclaredType);

            writer.WritePropertyName("dependencies");
            writer.WriteStartArray();
            foreach (AssetId dep in meta.Dependencies.OrderBy(d => d.Value))
            {
                writer.WriteStringValue(dep.ToCanonicalString());
            }
            writer.WriteEndArray();

            writer.WritePropertyName("importSettings");
            // Ensure importSettings is canonical as well
            WriteCanonical(writer, meta.ImportSettings);

            writer.WriteString("logicalName", meta.LogicalName);
            writer.WriteNumber("schemaVersion", meta.SchemaVersion);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string SerializeManifestCanonical(ContentManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", manifest.SchemaVersion);

            writer.WritePropertyName("entries");
            writer.WriteStartArray();

            foreach (ContentManifestEntry entry in manifest.Entries.OrderBy(e => e.AssetId.Value))
            {
                writer.WriteStartObject();
                writer.WriteString("artifactLocator", entry.ArtifactLocator);
                writer.WriteString("assetId", entry.AssetId.ToCanonicalString());
                writer.WriteString("declaredType", entry.DeclaredType);

                writer.WritePropertyName("dependencies");
                writer.WriteStartArray();
                foreach (AssetId dep in entry.Dependencies.OrderBy(d => d.Value))
                {
                    writer.WriteStringValue(dep.ToCanonicalString());
                }
                writer.WriteEndArray();

                writer.WriteString("importSettingsHash", entry.ImportSettingsHash);
                writer.WriteString("logicalName", entry.LogicalName);
                writer.WriteNumber("size", entry.Size);
                writer.WriteString("sourceHash", entry.SourceHash);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteCanonical(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                // Use invariant, raw text preserved? Need to normalize number formatting.
                // Get raw text and ensure invariant: use decimal?
                // For canonical, we will write the raw number but normalized via GetDouble then ToString invariant? Simpler: write raw text as is, but numbers from meta are ints.
                // We'll try to preserve canonical numeric representation: if it's integer, write as integer.
                if (element.TryGetInt64(out long l))
                {
                    writer.WriteNumberValue(l);
                }
                else if (element.TryGetDouble(out double d))
                {
                    // Use G17 invariant
                    writer.WriteNumberValue(d);
                }
                else
                {
                    writer.WriteRawValue(element.GetRawText());
                }
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    public static JsonElement ParseAndCanonicalize(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, DocOptions);
        string canonical = SerializeCanonical(doc.RootElement);
        using JsonDocument canonicalDoc = JsonDocument.Parse(canonical);
        return canonicalDoc.RootElement.Clone();
    }
}
