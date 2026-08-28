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
                // Lossless canonical: Int64 -> Decimal normalized -> raw normalized (exponent lowercased, + stripped)
                // This avoids double rounding and ensures 1e400 vs 1E400 are identical.
                if (element.TryGetInt64(out long l))
                {
                    writer.WriteNumberValue(l);
                }
                else if (element.TryGetDecimal(out decimal dec))
                {
                    // Normalize decimal: strip trailing zeros and use invariant, so 1.00 -> 1, 1.00e2 -> 100
                    // Use G29 to get minimal representation without scientific unless needed, then lower case
                    string decStr = dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    // Dec's ToString may use exponent for large/small, normalize to lower e and remove + 
                    decStr = decStr.Replace("E+", "e").Replace("E", "e").Replace("e+", "e");
                    // For decimal, ToString already strips trailing zeros for integer values? Ensure 1.0 -> 1
                    // If decStr contains '.' and ends with '0', decimal.ToString may keep it; normalize manually
                    if (decStr.Contains('.') && !decStr.Contains('e'))
                    {
                        decStr = decStr.TrimEnd('0').TrimEnd('.');
                        if (decStr == "-0") decStr = "0";
                        if (decStr.Length == 0) decStr = "0";
                    }
                    writer.WriteRawValue(decStr, skipInputValidation: true);
                }
                else
                {
                    // Outside decimal range (e.g. 1e400): normalize raw verbatim -> lower case, e+ -> e, remove leading zeros
                    string raw = element.GetRawText();
                    string normalized = raw.ToLowerInvariant().Replace("e+", "e");
                    // Further canonical: if raw is integer with leading zeros, keep as is? For now lowercased is enough for 1e400 vs 1E400
                    writer.WriteRawValue(normalized, skipInputValidation: true);
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
