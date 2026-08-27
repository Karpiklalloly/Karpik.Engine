using System.Text.Json;

namespace Karpik.Content.Core;

public sealed class AssetMeta : IEquatable<AssetMeta>
{
    public const int CurrentSchemaVersion = 1;
    public const string ExpectedDeclaredTypeRawJson = "raw-json";

    public int SchemaVersion { get; }
    public AssetId AssetId { get; }
    public string DeclaredType { get; }
    public string LogicalName { get; }
    public JsonElement ImportSettings { get; }
    public IReadOnlyList<AssetId> Dependencies { get; }

    // Raw canonical JSON bytes for hashing, preserved after parsing
    public string RawImportSettingsJson { get; }

    public AssetMeta(int schemaVersion, AssetId assetId, string declaredType, string logicalName, JsonElement importSettings, string rawImportSettingsJson, IReadOnlyList<AssetId> dependencies)
    {
        SchemaVersion = schemaVersion;
        AssetId = assetId;
        DeclaredType = declaredType;
        LogicalName = logicalName;
        ImportSettings = importSettings;
        RawImportSettingsJson = rawImportSettingsJson;
        Dependencies = dependencies;
    }

    public bool Equals(AssetMeta? other)
    {
        if (other is null) return false;
        return SchemaVersion == other.SchemaVersion && AssetId.Equals(other.AssetId) && DeclaredType == other.DeclaredType && LogicalName == other.LogicalName && RawImportSettingsJson == other.RawImportSettingsJson && Dependencies.SequenceEqual(other.Dependencies);
    }

    public override bool Equals(object? obj) => obj is AssetMeta other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(SchemaVersion, AssetId, DeclaredType, LogicalName, RawImportSettingsJson);

    public static AssetMeta Parse(string json, string? relativePathForDiagnostics, List<ContentDiagnostic> diagnostics)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidMetaJson, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, $"Invalid meta JSON: {ex.Message}"));
            throw new InvalidDataException($"Invalid meta JSON at {relativePathForDiagnostics}: {ex.Message}", ex);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidMetaJson, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, "Meta root must be an object."));
                throw new InvalidDataException($"Meta root must be object at {relativePathForDiagnostics}");
            }

            // schemaVersion
            if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVersionEl) || schemaVersionEl.ValueKind != JsonValueKind.Number || !schemaVersionEl.TryGetInt32(out int schemaVersion))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.MissingMetaField, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, "Missing or invalid 'schemaVersion'."));
                throw new InvalidDataException($"Missing schemaVersion at {relativePathForDiagnostics}");
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidMetaSchemaVersion, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, $"Unsupported meta schema version '{schemaVersion}'. Expected {CurrentSchemaVersion}."));
                throw new InvalidDataException($"Invalid schema version at {relativePathForDiagnostics}");
            }

            // assetId
            if (!root.TryGetProperty("assetId", out JsonElement assetIdEl) || assetIdEl.ValueKind != JsonValueKind.String)
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.MissingMetaField, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, "Missing 'assetId'."));
                throw new InvalidDataException($"Missing assetId at {relativePathForDiagnostics}");
            }

            string assetIdStr = assetIdEl.GetString()!;
            if (!AssetId.TryParse(assetIdStr, out AssetId assetId))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidAssetId, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, $"Invalid assetId '{assetIdStr}'. Expected GUID D format."));
                throw new InvalidDataException($"Invalid assetId at {relativePathForDiagnostics}");
            }

            // declaredType
            if (!root.TryGetProperty("declaredType", out JsonElement declaredTypeEl) || declaredTypeEl.ValueKind != JsonValueKind.String)
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.MissingMetaField, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, "Missing 'declaredType'."));
                throw new InvalidDataException($"Missing declaredType at {relativePathForDiagnostics}");
            }

            string declaredType = declaredTypeEl.GetString()!;
            if (string.IsNullOrWhiteSpace(declaredType))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.MissingMetaField, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, "Empty 'declaredType'."));
                throw new InvalidDataException($"Empty declaredType at {relativePathForDiagnostics}");
            }

            // logicalName
            if (!root.TryGetProperty("logicalName", out JsonElement logicalNameEl) || logicalNameEl.ValueKind != JsonValueKind.String)
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.MissingMetaField, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, "Missing 'logicalName'."));
                throw new InvalidDataException($"Missing logicalName at {relativePathForDiagnostics}");
            }

            string logicalName = logicalNameEl.GetString()!;
            if (string.IsNullOrWhiteSpace(logicalName))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.MissingMetaField, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, "Empty 'logicalName'."));
                throw new InvalidDataException($"Empty logicalName at {relativePathForDiagnostics}");
            }

            // logicalName validation: must contain '/', not traversal
            if (!IsValidLogicalName(logicalName))
            {
                diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidLogicalName, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, $"Invalid logicalName '{logicalName}'. Expected 'namespace/path' with allowed chars [a-z0-9_\\-/] and no traversal."));
                throw new InvalidDataException($"Invalid logicalName at {relativePathForDiagnostics}");
            }

            // importSettings (optional, default to empty object)
            JsonElement importSettings;
            string rawImportSettingsJson;
            if (root.TryGetProperty("importSettings", out JsonElement importSettingsEl))
            {
                importSettings = importSettingsEl.Clone();
                rawImportSettingsJson = CanonicalJson.SerializeCanonical(importSettings);
            }
            else
            {
                importSettings = JsonDocument.Parse("{}").RootElement.Clone();
                rawImportSettingsJson = "{}";
            }

            // dependencies (optional)
            var dependencies = new List<AssetId>();
            if (root.TryGetProperty("dependencies", out JsonElement depsEl))
            {
                if (depsEl.ValueKind != JsonValueKind.Array)
                {
                    diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidMetaJson, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, "'dependencies' must be an array."));
                    throw new InvalidDataException($"Invalid dependencies at {relativePathForDiagnostics}");
                }

                foreach (JsonElement depEl in depsEl.EnumerateArray())
                {
                    if (depEl.ValueKind != JsonValueKind.String)
                    {
                        diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidMetaJson, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, "Dependency entry must be string GUID."));
                        throw new InvalidDataException($"Invalid dependency at {relativePathForDiagnostics}");
                    }

                    string depStr = depEl.GetString()!;
                    if (!AssetId.TryParse(depStr, out AssetId depId))
                    {
                        diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidAssetId, ContentDiagnosticSeverity.Error, relativePathForDiagnostics, $"Invalid dependency assetId '{depStr}'."));
                        throw new InvalidDataException($"Invalid dependency assetId at {relativePathForDiagnostics}");
                    }

                    // normalize to lower-case but keep as AssetId
                    dependencies.Add(depId);
                }
            }

            return new AssetMeta(schemaVersion, assetId, logicalName: logicalName, declaredType: declaredType, importSettings: importSettings, rawImportSettingsJson: rawImportSettingsJson, dependencies: dependencies);
        }
    }

    public static bool IsValidLogicalName(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName)) return false;
        if (logicalName.Contains('\\')) return false;
        if (logicalName.StartsWith('/') || logicalName.EndsWith('/')) return false;
        if (logicalName.Contains("//")) return false;
        if (logicalName.Contains("..")) return false;
        if (!logicalName.Contains('/')) return false;
        // allow a-z, A-Z, 0-9, _, -, ., /
        foreach (char c in logicalName)
        {
            if (c == '/') continue;
            if (c >= 'a' && c <= 'z') continue;
            if (c >= 'A' && c <= 'Z') continue;
            if (c >= '0' && c <= '9') continue;
            if (c == '_' || c == '-' || c == '.') continue;
            return false;
        }

        // no segment empty, no segment "." or ".."
        string[] segs = logicalName.Split('/');
        foreach (string seg in segs)
        {
            if (seg.Length == 0) return false;
            if (seg == "." || seg == "..") return false;
        }

        return true;
    }

    public string ToCanonicalMetaJson()
    {
        // Produce canonical bytes for hashing: fixed property order, entries sorted
        // We do not include source path; only meta fields.
        // Use CanonicalJson helper to build object in fixed order.
        var obj = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["assetId"] = AssetId.ToCanonicalString(),
            ["declaredType"] = DeclaredType,
            ["dependencies"] = Dependencies.OrderBy(d => d.Value).Select(d => d.ToCanonicalString()).ToArray(),
            ["importSettings"] = JsonDocument.Parse(RawImportSettingsJson).RootElement,
            ["logicalName"] = LogicalName,
            ["schemaVersion"] = SchemaVersion
        };

        // Need to serialize with canonical ordering: keys sorted ordinal, but we already want fixed order.
        // We'll manually construct JSON with fixed order.
        return CanonicalJson.SerializeMetaCanonical(this);
    }
}
