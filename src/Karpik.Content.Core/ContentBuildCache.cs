using System.Security.Cryptography;
using System.Text.Json;

namespace Karpik.Content.Core;

internal sealed record CachedContentAsset(
    ContentManifestEntry Entry,
    string ReuseKey,
    string CookedHash,
    IReadOnlyList<ContentDiagnostic> Diagnostics);

/// <summary>Disposable build-time metadata. Runtime manifest/artifact formats remain independent of this index.</summary>
internal sealed class ContentBuildCache
{
    private const string FileName = ".karpik-content-cache.v1.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _outputRoot;
    private readonly Dictionary<AssetId, CachedContentAsset> _assets;

    private ContentBuildCache(string outputRoot, ContentManifest manifest, Dictionary<AssetId, CachedContentAsset> assets)
    {
        _outputRoot = outputRoot;
        Manifest = manifest;
        _assets = assets;
    }

    public ContentManifest Manifest { get; }

    public static string ComputeReuseKey(SourceEntry source, IContentProcessor processor, AssetTarget target) =>
        // A JSON string array provides unambiguous framing independent of machine endianness.
        ContentHashing.HashString(JsonSerializer.Serialize(new[]
        {
            "karpik-content-reuse-v1", source.RelativePath, ContentHashing.HashSourceBytes(source.SourceBytes),
            source.Meta.ToCanonicalMetaJson(), processor.DeclaredType, processor.Version, target.ToString()
        }));

    public static ContentBuildCache? TryLoad(string outputRoot, ContentBuildOptions options)
    {
        try
        {
            string indexPath = Path.Combine(outputRoot, FileName);
            string manifestPath = Path.Combine(outputRoot, "manifest.json");
            if (!IsOrdinaryPath(outputRoot, indexPath) || !IsOrdinaryPath(outputRoot, manifestPath)) return null;
            using FileStream stream = File.OpenRead(indexPath);
            CacheIndex? index = JsonSerializer.Deserialize<CacheIndex>(stream, JsonOptions);
            if (index is null || index.SchemaVersion != 1 || index.Namespace != options.Namespace ||
                index.Target != options.Target || index.Records is null || !IsDigest(index.ManifestHash)) return null;

            ContentManifest manifest = ContentManifest.LoadFromFile(manifestPath);
            if (ContentHashing.HashString(manifest.ToCanonicalJson()) != index.ManifestHash ||
                index.Records.Length != manifest.Entries.Count) return null;

            var manifestEntries = manifest.Entries.ToDictionary(e => e.AssetId);
            var assets = new Dictionary<AssetId, CachedContentAsset>();
            foreach (CacheRecord? record in index.Records)
            {
                if (record is null || !AssetId.TryParse(record.AssetId, out AssetId id) ||
                    record.AssetId != id.ToCanonicalString() || !IsDigest(record.ReuseKey) || !IsDigest(record.CookedHash) ||
                    record.Diagnostics is null || record.Diagnostics.Any(d => d is null ||
                        d.Severity != ContentDiagnosticSeverity.Warning || string.IsNullOrWhiteSpace(d.Code) ||
                        string.IsNullOrWhiteSpace(d.Message)) || !manifestEntries.TryGetValue(id, out ContentManifestEntry? entry) ||
                    entry.Size < 0 || !TryGetArtifactPath(outputRoot, entry.ArtifactLocator, out _) ||
                    !assets.TryAdd(id, new CachedContentAsset(entry, record.ReuseKey, record.CookedHash, record.Diagnostics)))
                    return null;
            }

            return new ContentBuildCache(outputRoot, manifest, assets);
        }
        catch (Exception exception) when (IsCacheReadFailure(exception))
        {
            return null;
        }
    }

    public bool TryGet(AssetId id, string reuseKey, string expectedLocator, out CachedContentAsset? asset)
    {
        asset = null;
        if (!_assets.TryGetValue(id, out CachedContentAsset? candidate) || candidate.ReuseKey != reuseKey ||
            candidate.Entry.ArtifactLocator != expectedLocator ||
            !TryGetArtifactPath(_outputRoot, expectedLocator, out string path) ||
            !ArtifactMatches(path, candidate.Entry.Size, candidate.CookedHash)) return false;

        asset = candidate;
        return true;
    }

    public static bool ArtifactMatches(string path, long size, string cookedHash)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return stream.Length == size && Convert.ToHexStringLower(SHA256.HashData(stream)) == cookedHash;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void TrySave(string outputRoot, ContentBuildOptions options, ContentManifest manifest,
        IReadOnlyList<CachedContentAsset> assets)
    {
        string temporaryPath = Path.Combine(outputRoot, FileName + ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var records = assets.OrderBy(a => a.Entry.AssetId).Select(a => new CacheRecord(
                a.Entry.AssetId.ToCanonicalString(), a.ReuseKey, a.CookedHash, a.Diagnostics.ToArray())).ToArray();
            var index = new CacheIndex(1, options.Namespace, options.Target,
                ContentHashing.HashString(manifest.ToCanonicalJson()), records);
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, index, JsonOptions);
            File.Move(temporaryPath, Path.Combine(outputRoot, FileName), overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Publication already succeeded. Missing/stale cache metadata only costs the next build time.
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool TryGetArtifactPath(string outputRoot, string? locator, out string path)
    {
        path = string.Empty;
        if (locator is null || !locator.StartsWith("artifacts/", StringComparison.Ordinal) ||
            !locator.EndsWith(".cooked", StringComparison.Ordinal)) return false;
        string digest = locator[(locator.LastIndexOf('/') + 1)..^7];
        if (!IsDigest(digest) || locator != ContentHashing.ComputeArtifactLocator(digest)) return false;
        path = Path.Combine(outputRoot, locator.Replace('/', Path.DirectorySeparatorChar));
        return IsOrdinaryPath(outputRoot, path);
    }

    private static bool IsOrdinaryPath(string root, string path)
    {
        if (!PathSafety.IsContained(root, path)) return false;
        string rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (PathSafety.IsReparsePoint(current)) return false;
            if (string.Equals(current, rootFull, PathSafety.PathComparison)) return true;
        }
        return false;
    }

    private static bool IsDigest(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsCacheReadFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or
        JsonException or InvalidDataException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException;

    // Records bind to entries in the manifest via AssetId and the index-wide canonical manifest hash.
    private sealed record CacheIndex(int SchemaVersion, string Namespace, AssetTarget Target, string ManifestHash, CacheRecord[] Records);
    private sealed record CacheRecord(string AssetId, string ReuseKey, string CookedHash, ContentDiagnostic[] Diagnostics);
}
