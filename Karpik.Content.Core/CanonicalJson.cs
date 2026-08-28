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
                    // Normalize decimal: strip trailing zeros and use invariant, so 1.00 -> 1
                    string decStr = dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    decStr = decStr.Replace("E+", "e").Replace("E", "e").Replace("e+", "e");
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
                    // Outside decimal range (e.g. 1e400, 1.0e400, 10e399): full mantissa/exponent canonical
                    string raw = element.GetRawText();
                    string normalized = NormalizeLargeExponentNumber(raw);
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

    private static string NormalizeLargeExponentNumber(string raw)
    {
        string s = raw.ToLowerInvariant().Replace("e+", "e");
        int eIdx = s.IndexOf('e');
        if (eIdx < 0) return s;
        string mant = s.Substring(0, eIdx);
        string expPart = s.Substring(eIdx + 1);
        if (!long.TryParse(expPart, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out long exp))
        {
            // Exponent outside Int64 - still need to normalize mantissa case, but keep exponent as string lowercased
            // For very large exponent, we cannot parse, so just return lowercased raw with e+ -> e
            return s;
        }
        bool neg = mant.StartsWith("-");
        if (neg) mant = mant.Substring(1);
        // Split mantissa into integer and fractional
        int dot = mant.IndexOf('.');
        string intPart, fracPart;
        if (dot >= 0)
        {
            intPart = mant.Substring(0, dot);
            fracPart = mant.Substring(dot + 1);
        }
        else
        {
            intPart = mant;
            fracPart = "";
        }
        string originalIntPart = intPart;
        intPart = intPart.TrimStart('0');
        // Combine intPart + fracPart as digits, track exponent adjustment for dot
        string digits = intPart + fracPart;
        digits = digits.TrimStart('0');
        if (digits.Length == 0) return "0";
        // Adjust exponent for fractional part and leading zeros
        if (originalIntPart.TrimStart('0').Length == 0)
        {
            // intPart is all zeros (e.g. 0.001): find first non-zero in fracPart
            int firstNonZero = -1;
            for (int i = 0; i < fracPart.Length; i++) if (fracPart[i] != '0') { firstNonZero = i; break; }
            if (firstNonZero >= 0)
                exp = exp - (firstNonZero + 1);
        }
        else
        {
            // intPart non-zero: normalize exponent by intPart significant length
            exp = exp + (originalIntPart.TrimStart('0').Length - 1);
        }
        string first = digits.Substring(0, 1);
        string rest = digits.Length > 1 ? digits.Substring(1) : "";
        rest = rest.TrimEnd('0');
        string normMant = rest.Length > 0 ? first + "." + rest : first;
        if (neg) normMant = "-" + normMant;
        if (exp == 0) return normMant;
        return normMant + "e" + exp.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static JsonElement ParseAndCanonicalize(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, DocOptions);
        string canonical = SerializeCanonical(doc.RootElement);
        using JsonDocument canonicalDoc = JsonDocument.Parse(canonical);
        return canonicalDoc.RootElement.Clone();
    }
}
