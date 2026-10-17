using System.Text.Json;

namespace Karpik.Content.Core;

public sealed class ContentManifestEntry(
    AssetId assetId,
    string declaredType,
    string logicalName,
    string importSettingsHash,
    string sourceHash,
    string artifactLocator,
    long size,
    IReadOnlyList<AssetId> dependencies)
    : IEquatable<ContentManifestEntry>
{
    public AssetId AssetId { get; } = assetId;
    public string DeclaredType { get; } = declaredType;
    public string LogicalName { get; } = logicalName;
    public string ImportSettingsHash { get; } = importSettingsHash;
    public string SourceHash { get; } = sourceHash;
    public string ArtifactLocator { get; } = artifactLocator;
    public long Size { get; } = size;
    public IReadOnlyList<AssetId> Dependencies { get; } = dependencies;

    public bool Equals(ContentManifestEntry? other)
    {
        if (other is null) return false;
        return AssetId.Equals(other.AssetId)
               && DeclaredType == other.DeclaredType
               && LogicalName == other.LogicalName
               && ImportSettingsHash == other.ImportSettingsHash
               && SourceHash == other.SourceHash
               && ArtifactLocator == other.ArtifactLocator
               && Size == other.Size
               && Dependencies.SequenceEqual(other.Dependencies);
    }

    public override bool Equals(object? obj) => obj is ContentManifestEntry other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            AssetId,
            DeclaredType,
            LogicalName,
            ImportSettingsHash,
            SourceHash,
            ArtifactLocator,
            Size);
}

public sealed class ContentManifest(int schemaVersion, IReadOnlyList<ContentManifestEntry> entries)
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; } = schemaVersion;
    public IReadOnlyList<ContentManifestEntry> Entries { get; } = entries;

    public string ToCanonicalJson() => CanonicalJson.SerializeManifestCanonical(this);

    public byte[] ToCanonicalBytes() => System.Text.Encoding.UTF8.GetBytes(ToCanonicalJson());

    public static ContentManifest Parse(string json, List<ContentDiagnostic>? diagnostics = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            diagnostics?.Add(new ContentDiagnostic(
                ContentDiagnosticCodes.ManifestCorrupt,
                ContentDiagnosticSeverity.Error,
                null,
                $"Invalid manifest JSON: {ex.Message}"));
            throw new InvalidDataException($"Invalid manifest JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Manifest root must be object.");
            }

            if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVersionEl)
                || !schemaVersionEl.TryGetInt32(out int schemaVersion))
            {
                throw new InvalidDataException("Missing schemaVersion in manifest.");
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                diagnostics?.Add(
                    new ContentDiagnostic(
                        ContentDiagnosticCodes.InvalidManifestSchemaVersion,
                        ContentDiagnosticSeverity.Error,
                        null,
                        $"Unsupported manifest schema version '{schemaVersion}'."));
                throw new InvalidDataException($"Invalid manifest schema version {schemaVersion}");
            }

            if (!root.TryGetProperty("entries", out JsonElement entriesEl)
                || entriesEl.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Missing entries array in manifest.");
            }

            var entries = new List<ContentManifestEntry>();
            foreach (JsonElement entryEl in entriesEl.EnumerateArray())
            {
                if (entryEl.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Manifest entry must be object.");
                }

                string assetIdStr = entryEl.GetProperty("assetId").GetString()!;
                string declaredType = entryEl.GetProperty("declaredType").GetString()!;
                string logicalName = entryEl.GetProperty("logicalName").GetString()!;
                string importSettingsHash = entryEl.GetProperty("importSettingsHash").GetString()!;
                string sourceHash = entryEl.GetProperty("sourceHash").GetString()!;
                string artifactLocator = entryEl.GetProperty("artifactLocator").GetString()!;
                long size = entryEl.GetProperty("size").GetInt64();

                var deps = new List<AssetId>();
                if (entryEl.TryGetProperty("dependencies", out JsonElement depsEl)
                    && depsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement depEl in depsEl.EnumerateArray())
                    {
                        string depStr = depEl.GetString()!;
                        deps.Add(AssetId.Parse(depStr));
                    }
                }

                entries.Add(new ContentManifestEntry(
                    AssetId.Parse(assetIdStr),
                    declaredType,
                    logicalName,
                    importSettingsHash,
                    sourceHash,
                    artifactLocator,
                    size,
                    [.. deps.OrderBy(d => d.Value)]));
            }

            // Ensure sorted
            entries.Sort((a, b) => a.AssetId.CompareTo(b.AssetId));

            return new ContentManifest(schemaVersion, entries);
        }
    }

    public static ContentManifest LoadFromFile(string path, List<ContentDiagnostic>? diagnostics = null)
    {
        using Stream stream = File.OpenRead(path);
        return Load(stream, diagnostics);
    }

    public static ContentManifest Load(Stream stream, List<ContentDiagnostic>? diagnostics = null)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 1024, true);
        return Parse(reader.ReadToEnd(), diagnostics);
    }

    public void SaveToFile(string path)
    {
        string json = ToCanonicalJson();
        // Ensure UTF-8 without BOM
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
    }
}
