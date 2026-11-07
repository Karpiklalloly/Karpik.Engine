using System.Diagnostics;
using System.Text.Json;

namespace Karpik.Content.Core;

public readonly record struct ContentCollectionStatus(bool Enabled, string FileSystem, string? Reason);

/// <summary>An immutable filesystem generation retained by a crash-released OS ownership claim. Cold-path only.</summary>
public sealed class ContentGeneration : IDisposable
{
    internal const string MarkerName = ".karpik-current-generation";
    private FileStream? _claim;
    private readonly string _outputRoot;
    private ContentGeneration(string outputRoot, string id, string root, FileStream claim)
    {
        _outputRoot = outputRoot;
        GenerationId = id;
        RootPath = root;
        _claim = claim;
        try
        {
            (Manifest, var descriptor) = ReadMetadata(outputRoot, root);
            IdentityKnown = descriptor.IdentityKnown;
            Namespace = descriptor.Namespace;
            Target = descriptor.Target;
        }
        catch { claim.Dispose(); throw; }
    }
    public bool IdentityKnown { get; }
    public static ContentGeneration OpenOrImportCurrent(string outputRoot)
    {
        outputRoot = Path.GetFullPath(outputRoot);
        using var gate = OpenGate(outputRoot);
        string id = ImportLegacyUnderGate(outputRoot) ?? throw new FileNotFoundException("Content manifest is missing.");
        return OpenUnderGate(outputRoot, id);
    }
    public string Namespace { get; }
    public AssetTarget Target { get; }
    public string GenerationId { get; }
    public string RootPath { get; }
    public ContentManifest Manifest { get; }

    public static ContentGeneration OpenCurrent(string outputRoot)
    {
        outputRoot = Path.GetFullPath(outputRoot);
        using var gate = OpenGate(outputRoot);
        string id = ReadCurrentId(outputRoot) ?? throw new InvalidDataException("No immutable Content generation has been published.");
        var generation = OpenUnderGate(outputRoot, id);
        try
        {
            // A crash during the legacy projection cannot bind a mismatched manifest to immutable bytes.
            if (ContentManifest.LoadFromFile(Path.Combine(outputRoot, "manifest.json")).ToCanonicalJson() != generation.Manifest.ToCanonicalJson())
                throw new InvalidDataException("Content publication was interrupted; rebuild to recover the current projection.");
            return generation;
        }
        catch { generation.ReleaseClaim(); throw; }
    }

    public static ContentGeneration Open(string outputRoot, string generationId)
    {
        outputRoot = Path.GetFullPath(outputRoot);
        using var gate = OpenGate(outputRoot);
        return OpenUnderGate(outputRoot, generationId);
    }

    /// <summary>
    /// Read-only validation for ordinary packaged startup when ownership cannot be acquired.
    /// This acquires no claim and does not authorize reload or protect against concurrent publication.
    /// </summary>
    public static void ValidateCurrentProjection(string outputRoot)
    {
        outputRoot = Path.GetFullPath(outputRoot);
        string id = ReadCurrentId(outputRoot) ?? throw new InvalidDataException("No immutable Content generation has been published.");
        var metadata = ReadMetadata(outputRoot, Path.Combine(outputRoot, ".generations", id));
        if (ContentManifest.LoadFromFile(Path.Combine(outputRoot, "manifest.json")).ToCanonicalJson() != metadata.Manifest.ToCanonicalJson())
            throw new InvalidDataException("Content publication was interrupted; rebuild to recover the current projection.");
    }

    internal static ContentGeneration OpenUnderGate(string outputRoot, string id)
    {
        ValidateId(id);
        string root = Path.Combine(outputRoot, ".generations", id);
        EnsureOrdinaryPath(outputRoot, root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Content generation was already collected.");
        string claims = Path.Combine(root, ".claims");
        EnsureOrdinaryPath(outputRoot, claims);
        Directory.CreateDirectory(claims);
        string claimPath = Path.Combine(claims, Guid.NewGuid().ToString("N") + ".claim");
        return new ContentGeneration(outputRoot, id, root,
            new FileStream(claimPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _claim, null) is not { } claim) return;
        claim.Dispose();
        // Collection is cold and best effort; uncertainty always retains bytes.
        try { CollectUnused(_outputRoot); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
    }

    private void ReleaseClaim() => Interlocked.Exchange(ref _claim, null)?.Dispose();

    public static int CollectUnused(string outputRoot)
    {
        outputRoot = Path.GetFullPath(outputRoot);
        using var gate = OpenGate(outputRoot);
        return CollectUnderGate(outputRoot);
    }

    internal static int CollectUnderGate(string outputRoot)
    {
        if (!GetCollectionStatus(outputRoot).Enabled) return 0;
        string? current;
        try { current = ReadCurrentId(outputRoot); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { return 0; }
        // Without a trustworthy current marker, even an apparently unowned generation may be the last good publication.
        if (current is null) return 0;
        string generations = Path.Combine(outputRoot, ".generations");
        if (!Directory.Exists(generations)) return 0;
        string[] candidates;
        try { candidates = Directory.GetDirectories(generations); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return 0; }
        int collected = 0;
        foreach (string root in candidates)
        {
            string id = Path.GetFileName(root);
            if (id.EndsWith(".pending", StringComparison.Ordinal) && Guid.TryParseExact(id[..^8], "N", out _))
            {
                try
                {
                    // Publishers hold the namespace gate throughout staging; no live publisher can own this candidate.
                    EnsureOrdinaryPath(outputRoot, root);
                    EnsureOrdinaryTree(outputRoot, root);
                    Directory.Delete(root, recursive: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
                continue;
            }
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            try
            {
                EnsureOrdinaryPath(outputRoot, root);
                _ = ReadMetadata(outputRoot, root); // Unknown/corrupt generation schemas cannot authorize reclamation.
                string claims = Path.Combine(root, ".claims");
                EnsureOrdinaryPath(outputRoot, claims);
                bool owned = false;
                if (Directory.Exists(claims))
                {
                    foreach (string path in Directory.EnumerateFileSystemEntries(claims))
                    {
                        try
                        {
                            EnsureOrdinaryPath(outputRoot, path);
                            string name = Path.GetFileNameWithoutExtension(path);
                            if (Directory.Exists(path) || !path.EndsWith(".claim", StringComparison.Ordinal) || !Guid.TryParseExact(name, "N", out _))
                            { owned = true; continue; }
                            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                            File.Delete(path); // Prune stale claims even while another owner retains the generation.
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { owned = true; }
                    }
                }
                if (!owned && id != current)
                {
                    EnsureOrdinaryTree(outputRoot, root);
                    Directory.Delete(root, recursive: true);
                    collected++;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        return collected;
    }

    internal static string? ImportLegacyUnderGate(string outputRoot)
    {
        string? current = ReadCurrentId(outputRoot);
        if (current != null) return current;
        string legacyManifest = Path.Combine(outputRoot, "manifest.json");
        EnsureOrdinaryPath(outputRoot, legacyManifest);
        if (!File.Exists(legacyManifest)) return null;
        var manifest = ContentManifest.LoadFromFile(legacyManifest);
        string id = Guid.NewGuid().ToString("N");
        string generations = Path.Combine(outputRoot, ".generations");
        EnsureOrdinaryPath(outputRoot, generations);
        Directory.CreateDirectory(generations);
        string pending = Path.Combine(generations, id + ".pending");
        Directory.CreateDirectory(pending);
        // Legacy manifests do not establish target/namespace provenance. Copy only referenced artifact files.
        foreach (var entry in manifest.Entries)
        {
            string source = Path.GetFullPath(Path.Combine(outputRoot, entry.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar)));
            string destination = Path.GetFullPath(Path.Combine(pending, entry.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar)));
            EnsureOrdinaryPath(outputRoot, source);
            EnsureOrdinaryPath(pending, destination);
            if (!entry.ArtifactLocator.StartsWith("artifacts/", StringComparison.Ordinal) || entry.Size < 0 || new FileInfo(source).Length != entry.Size)
                throw new InvalidDataException("Legacy Content artifact is missing, unsafe or has the wrong size.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination)) File.Copy(source, destination);
            using var original = File.OpenRead(source);
            using var copied = File.OpenRead(destination);
            if (!System.Security.Cryptography.SHA256.HashData(original).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(copied)))
                throw new InvalidDataException("Legacy Content artifact changed during capture.");
        }
        manifest.SaveToFile(Path.Combine(pending, "manifest.json"));
        string fingerprint = Path.Combine(outputRoot, ".karpik-content-inputs.v1");
        EnsureOrdinaryPath(outputRoot, fingerprint);
        if (File.Exists(fingerprint)) File.Copy(fingerprint, Path.Combine(pending, Path.GetFileName(fingerprint)));
        File.WriteAllText(Path.Combine(pending, "generation.json"), JsonSerializer.Serialize(new Descriptor(1,
            string.Empty, AssetTarget.Shared, ContentHashing.HashString(manifest.ToCanonicalJson()), IdentityKnown: false)));
        Directory.Move(pending, Path.Combine(generations, id));
        // This is the rollback anchor before any legacy path may be overwritten or removed.
        WriteCurrentMarker(outputRoot, id);
        return id;
    }

    private static void WriteCurrentMarker(string outputRoot, string id)
    {
        string marker = Path.Combine(outputRoot, MarkerName);
        EnsureOrdinaryPath(outputRoot, marker);
        if (Directory.Exists(marker)) throw new InvalidDataException("Current Content generation marker is a directory.");
        string temporary = marker + ".tmp-" + id;
        File.WriteAllText(temporary, id + "\n");
        File.Move(temporary, marker, overwrite: true);
    }

    internal static bool CanReuseUnderGate(string outputRoot, string id, ContentBuildOptions options,
        ContentManifest manifest, IReadOnlyDictionary<string, CachedContentAsset> artifacts)
    {
        string root = Path.Combine(outputRoot, ".generations", id);
        var metadata = ReadMetadata(outputRoot, root);
        if (!metadata.Descriptor.IdentityKnown || metadata.Descriptor.Namespace != options.Namespace || metadata.Descriptor.Target != options.Target ||
            metadata.Manifest.ToCanonicalJson() != manifest.ToCanonicalJson()) return false;
        foreach (var entry in manifest.Entries)
        {
            if (!artifacts.TryGetValue(entry.ArtifactLocator, out var cached) ||
                !ContentBuildCache.ArtifactMatches(Path.Combine(root, entry.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar)), entry.Size, cached.CookedHash)) return false;
        }
        return true;
    }

    internal static string PublishUnderGate(string outputRoot, string stagingRoot, ContentBuildOptions options, ContentManifest manifest, out ContentGeneration? retained)
    {
        string id = Guid.NewGuid().ToString("N");
        string generations = Path.Combine(outputRoot, ".generations");
        EnsureOrdinaryPath(outputRoot, generations);
        Directory.CreateDirectory(generations);
        string pending = Path.Combine(generations, id + ".pending");
        CopyTree(stagingRoot, pending);
        File.WriteAllText(Path.Combine(pending, "generation.json"), JsonSerializer.Serialize(new Descriptor(1,
            options.Namespace, options.Target, ContentHashing.HashString(manifest.ToCanonicalJson()))));
        Directory.Move(pending, Path.Combine(generations, id));
        // Acquire requested delivery ownership before switching the published result.
        retained = options.RetainGeneration ? OpenUnderGate(outputRoot, id) : null;
        try
        {
            // The generation exists before any mutable legacy projection is touched.
            new ContentAtomicPublisher(outputRoot).Publish(stagingRoot);
            WriteCurrentMarker(outputRoot, id);
            return id;
        }
        catch
        {
            retained?.ReleaseClaim(); // Already under namespace gate: do not reenter collection.
            retained = null;
            throw;
        }
    }

    internal static void RecoverProjectionUnderGate(string outputRoot)
    {
        string? id = ReadCurrentId(outputRoot);
        if (id is null) return;
        string generation = Path.Combine(outputRoot, ".generations", id);
        string expected = ContentManifest.LoadFromFile(Path.Combine(generation, "manifest.json")).ToCanonicalJson();
        try
        {
            if (ContentManifest.LoadFromFile(Path.Combine(outputRoot, "manifest.json")).ToCanonicalJson() == expected) return;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or FormatException or JsonException or OverflowException) { }
        string staging = Path.Combine(outputRoot, ".staging", Guid.NewGuid().ToString("N"));
        CopyTree(generation, staging);
        File.Delete(Path.Combine(staging, "generation.json"));
        if (Directory.Exists(Path.Combine(staging, ".claims"))) Directory.Delete(Path.Combine(staging, ".claims"), true);
        new ContentAtomicPublisher(outputRoot).Publish(staging);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string path in Directory.EnumerateFileSystemEntries(source))
        {
            EnsureOrdinaryPath(source, path);
            if (Path.GetFileName(path) == ".claims") continue;
            string target = Path.Combine(destination, Path.GetFileName(path));
            if (Directory.Exists(path)) CopyTree(path, target);
            else File.Copy(path, target);
        }
    }

    private sealed record Descriptor(int SchemaVersion, string Namespace, AssetTarget Target, string ManifestHash, bool IdentityKnown = true);

    internal static string? ReadCurrentId(string outputRoot)
    {
        string marker = Path.Combine(outputRoot, MarkerName);
        EnsureOrdinaryPath(outputRoot, marker);
        if (Directory.Exists(marker)) throw new InvalidDataException("Current Content generation marker is a directory.");
        if (!File.Exists(marker)) return null;
        string id = File.ReadAllText(marker).Trim();
        ValidateId(id);
        string root = Path.Combine(outputRoot, ".generations", id);
        EnsureOrdinaryPath(outputRoot, root);
        if (!File.Exists(Path.Combine(root, "manifest.json"))) throw new InvalidDataException("Current Content generation is missing.");
        _ = ReadMetadata(outputRoot, root); // A damaged current pointer must not authorize deleting the last usable older generation.
        return id;
    }

    internal static FileStream OpenGate(string outputRoot)
    {
        EnsureOrdinaryPath(outputRoot, outputRoot);
        Directory.CreateDirectory(outputRoot);
        string path = Path.Combine(outputRoot, ".karpik-generations.gate");
        EnsureOrdinaryPath(outputRoot, path);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is not (19 or 30) && elapsed.Elapsed < TimeSpan.FromSeconds(30)) { Thread.Sleep(2); }
        }
    }

    internal static void EnsureOrdinaryPath(string outputRoot, string path)
    {
        if (!PathSafety.IsContained(outputRoot, path)) throw new InvalidDataException("Content path escapes namespace.");
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if (PathSafety.IsReparsePoint(current)) throw new InvalidDataException("Content namespace cannot traverse a symbolic link or reparse point.");
            current = Path.GetDirectoryName(current);
        }
    }

    private static void EnsureOrdinaryTree(string outputRoot, string root)
    {
        // Reject links before recursion; never follow an unverified subtree while collecting.
        foreach (string path in Directory.EnumerateFileSystemEntries(root))
        {
            EnsureOrdinaryPath(outputRoot, path);
            if (Directory.Exists(path)) EnsureOrdinaryTree(outputRoot, path);
        }
    }

    public static ContentCollectionStatus GetCollectionStatus(string outputRoot)
    {
        try
        {
            outputRoot = Path.GetFullPath(outputRoot);
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
                return new(false, string.Empty, "Generation collection has not been validated on this platform.");
            var drive = DriveInfo.GetDrives().Where(d => PathSafety.IsContained(d.RootDirectory.FullName, outputRoot))
                .OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
            string format = drive?.DriveFormat.ToLowerInvariant() ?? string.Empty;
            // WP-01 evidence covers ext4/tmpfs and Windows NTFS. Unknown/remote filesystems retain data.
            bool enabled = OperatingSystem.IsLinux() ? format is "ext4" or "tmpfs" : format == "ntfs";
            return new(enabled, format, enabled ? null : "Generation collection is disabled because this filesystem has no validated ownership protocol.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(false, string.Empty, "Generation collection is disabled because filesystem ownership could not be established: " + error.Message);
        }
    }

    private static (ContentManifest Manifest, Descriptor Descriptor) ReadMetadata(string outputRoot, string root)
    {
        try
        {
            string manifestPath = Path.Combine(root, "manifest.json"), descriptorPath = Path.Combine(root, "generation.json");
            EnsureOrdinaryPath(outputRoot, manifestPath);
            EnsureOrdinaryPath(outputRoot, descriptorPath);
            var manifest = ContentManifest.LoadFromFile(manifestPath);
            var descriptor = JsonSerializer.Deserialize<Descriptor>(File.ReadAllText(descriptorPath))
                ?? throw new InvalidDataException("Missing generation descriptor.");
            if (descriptor.SchemaVersion != 1 || descriptor.ManifestHash != ContentHashing.HashString(manifest.ToCanonicalJson()) ||
                (descriptor.IdentityKnown ? string.IsNullOrWhiteSpace(descriptor.Namespace) : descriptor.Namespace != string.Empty || descriptor.Target != AssetTarget.Shared) || descriptor.Target is not (AssetTarget.Client or AssetTarget.Server or AssetTarget.Shared))
                throw new InvalidDataException("Invalid generation descriptor.");
            foreach (var entry in manifest.Entries)
                EnsureOrdinaryPath(root, Path.GetFullPath(Path.Combine(root, entry.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar))));
            return (manifest, descriptor);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("Invalid Content generation metadata.", error);
        }
    }

    private static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid Content generation identifier.");
    }
}
