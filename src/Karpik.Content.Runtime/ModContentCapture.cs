using System.Buffers;
using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

/// <summary>One initial claim on a validated local mod package. Consumers retain their own claims.</summary>
public sealed class ModContentCapture : IDisposable
{
    private const long MaximumCaptureBytes = 4L * 1024 * 1024 * 1024;
    private static long _reservedBytes;
    private int _disposed;
    private ModContentCapture(ModContentPackage package) => Package = package;
    public ModContentPackage Package { get; }

    /// <summary>Returns null only for a missing side directory. The caller must retain <paramref name="previous"/>'s store until this call finishes.</summary>
    public static Task<ModContentCapture?> CaptureAsync(string modDirectory, string modId, AssetTarget target,
        ModContentPackage? previous = null, Action<ContentDiagnostic>? report = null, CancellationToken ct = default) =>
        Task.Run(() => CaptureCoreAsync(modDirectory, modId, target, previous, report, ct), ct);

    private static async Task<ModContentCapture?> CaptureCoreAsync(string modDirectory, string modId, AssetTarget target,
        ModContentPackage? previous, Action<ContentDiagnostic>? report, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        string side = target switch
        {
            AssetTarget.Client => "Client",
            AssetTarget.Server => "Server",
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        modDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modDirectory));
        string source = Path.Combine(modDirectory, "Content", side);
        if (!ContentCaptureFiles.DirectoryExists(source)) return null;
        var metadata = ContentGeneration.ReadPackageMetadata(source, modId, target, ct);
        string declarationPath = Path.Combine(source, "overrides.json");
        byte[]? declaration = ContentCaptureFiles.ReadMetadata(declarationPath, ct);
        var rules = ModContentDeclaration.Parse(declaration, declarationPath, metadata.GenerationId, report, ct);
        var identity = new ModContentCaptureIdentity(metadata.GenerationId, metadata.Fingerprint,
            declaration is null ? null : ContentHashing.HashBytes(declaration));

        void RecheckMetadata()
        {
            byte[]? currentDeclaration = ContentCaptureFiles.ReadMetadata(declarationPath, ct);
            if ((declaration is null) != (currentDeclaration is null) || !declaration.AsSpan().SequenceEqual(currentDeclaration) ||
                metadata.Fingerprint != ContentGeneration.ReadPackageMetadata(source, modId, target, ct).Fingerprint)
                throw new InvalidDataException($"Mod Content metadata changed during capture: '{source}'.");
            ct.ThrowIfCancellationRequested();
        }

        var artifacts = new Dictionary<string, ContentManifestEntry>(StringComparer.Ordinal);
        long bytes = 0;
        foreach (var entry in metadata.Manifest.Entries)
        {
            ContentCaptureFiles.ResolveArtifact(metadata.RootPath, entry.ArtifactLocator);
            if (entry.Size < 0) throw new InvalidDataException($"Invalid cooked artifact size: '{entry.ArtifactLocator}'.");
            if (!artifacts.TryAdd(entry.ArtifactLocator, entry))
            {
                if (artifacts[entry.ArtifactLocator].Size != entry.Size)
                    throw new InvalidDataException($"Conflicting cooked artifact sizes: '{entry.ArtifactLocator}'.");
                continue;
            }
            if (entry.Size > MaximumCaptureBytes - bytes) throw BudgetExceeded(source);
            bytes += entry.Size;
        }

        if (previous?.CaptureIdentity == identity && previous.ModId == modId && previous.SourcePath == source)
        {
            foreach (var entry in artifacts.Values)
                using (OpenArtifact(metadata.RootPath, entry, ct)) { }
            RecheckMetadata();
            ContentStoreLifetime.Retain(previous.Store);
            return new ModContentCapture(previous);
        }

        string modsRoot = Path.GetDirectoryName(modDirectory)!;
        string tempRoot = Path.GetFullPath(Path.GetTempPath());
        string relativeTemp = Path.GetRelativePath(modsRoot, tempRoot);
        if (!Path.IsPathRooted(relativeTemp) && relativeTemp != ".." &&
            !relativeTemp.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Mod Content staging must be outside Mods.");
        ContentCaptureFiles.DirectoryExists(tempRoot);
        ct.ThrowIfCancellationRequested();
        Reserve(bytes, source);
        string? captureRoot = null;
        try
        {
            captureRoot = Directory.CreateTempSubdirectory("karpik-mod-content-").FullName;
            foreach (var entry in artifacts.Values)
            {
                string output = ContentCaptureFiles.ResolveArtifact(captureRoot, entry.ArtifactLocator);
                using var file = OpenArtifact(metadata.RootPath, entry, ct);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await CopyArtifactAsync(file, destination, entry.Size, ct).ConfigureAwait(false);
            }
            RecheckMetadata();
            var store = new ModContentStore(captureRoot, bytes);
            var manifest = new ContentManifest(metadata.Manifest.SchemaVersion, Array.AsReadOnly(metadata.Manifest.Entries.Select(entry =>
                new ContentManifestEntry(entry.AssetId, entry.DeclaredType, entry.LogicalName, entry.ImportSettingsHash, entry.SourceHash,
                    entry.ArtifactLocator, entry.Size, Array.AsReadOnly(entry.Dependencies.ToArray()))).ToArray()));
            return new ModContentCapture(new ModContentPackage(modId, manifest, store, rules, source) { CaptureIdentity = identity });
        }
        catch
        {
            DeleteOwnedCapture(captureRoot, bytes);
            throw;
        }
    }

    private static InvalidDataException BudgetExceeded(string source) => new($"Mod Content staging budget exceeded: '{source}'.");

    private static void Reserve(long bytes, string source)
    {
        while (true)
        {
            long current = Volatile.Read(ref _reservedBytes);
            if (bytes > MaximumCaptureBytes - current) throw BudgetExceeded(source);
            if (Interlocked.CompareExchange(ref _reservedBytes, current + bytes, current) == current) return;
        }
    }

    internal static void DeleteOwnedCapture(string? root, long bytes)
    {
        // If cleanup fails, report it and conservatively leave its reservation charged.
        if (root is not null) Directory.Delete(root, recursive: true);
        Interlocked.Add(ref _reservedBytes, -bytes);
    }

    private static async Task CopyArtifactAsync(Stream source, Stream destination, long size, CancellationToken ct)
    {
        const int chunkSize = 64 * 1024;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        try
        {
            long remaining = size;
            while (remaining > 0)
            {
                int count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(chunkSize, remaining)), ct).ConfigureAwait(false);
                if (count == 0) throw new InvalidDataException("Cooked artifact shrank during capture.");
                await destination.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                remaining -= count;
            }
            if (await source.ReadAsync(buffer.AsMemory(0, 1), ct).ConfigureAwait(false) != 0)
                throw new InvalidDataException("Cooked artifact grew during capture.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static FileStream OpenArtifact(string root, ContentManifestEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string path = ContentCaptureFiles.ResolveArtifact(root, entry.ArtifactLocator);
        var file = ContentCaptureFiles.OpenRead(path) ?? throw new FileNotFoundException($"Missing cooked artifact '{path}'.", path);
        try
        {
            if (file.Length != entry.Size) throw new InvalidDataException($"Cooked artifact size mismatch: '{path}'.");
            return file;
        }
        catch { file.Dispose(); throw; }
    }

    /// <summary>Disposes every wrapper; any cleanup errors are aggregated with the preceding failure, if supplied.</summary>
    /// <remarks>When cleanup succeeds, the caller remains responsible for rethrowing any preceding failure.</remarks>
    public static void DisposeAll(IReadOnlyList<ModContentCapture> captures, Exception? primaryFailure = null)
    {
        List<Exception>? cleanupFailures = null;
        foreach (var capture in captures)
        {
            try { capture.Dispose(); }
            catch (Exception error) { (cleanupFailures ??= []).Add(error); }
        }
        if (cleanupFailures is null) return;
        if (primaryFailure is not null) cleanupFailures.Insert(0, primaryFailure);
        throw new AggregateException("Failed to release mod Content captures.", cleanupFailures);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) ContentStoreLifetime.Release(Package.Store);
    }
}
