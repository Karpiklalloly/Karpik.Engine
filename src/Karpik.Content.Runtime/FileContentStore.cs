using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;

namespace Karpik.Content.Runtime;

[Export(typeof(IContentStore))]
[Export(typeof(FileContentStore))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class FileContentStore : IContentStore, IContentStoreLifetime, IDisposable
{
    private readonly IFileSystem fileSystem;
    private readonly string _root;
    private readonly GenerationContentStore? _captured;
    public FileContentStore(IFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
        _root = Path.GetFullPath(fileSystem.ContentPath);
        if (fileSystem.Exists(Path.Combine(_root, ".karpik-current-generation")))
            _captured = GenerationContentStore.OpenCurrent(_root);
        else if (Directory.Exists(Path.Combine(_root, ".karpik-current-generation")))
            throw new InvalidDataException("Invalid Content generation marker.");
        else if (File.Exists(Path.Combine(_root, "manifest.json")))
        {
            try { _captured = GenerationContentStore.OpenOrImportCurrent(_root); }
            catch (UnauthorizedAccessException) { /* Read-only legacy packages retain their non-development filesystem workflow. */ }
            catch (IOException error) when ((error.HResult & 0xffff) is 19 or 30) { /* Write-protected Windows/Unix legacy package. */ }
        }
        else if (fileSystem.ExistsDirectory(Path.Combine(_root, ".generations")))
            throw new InvalidDataException("Incomplete immutable Content publication; rebuild before starting the runtime.");
    }
    public bool IdentityKnown => _captured?.IdentityKnown ?? false;
    public bool IsGenerationCaptured => _captured != null;
    public Karpik.Content.Core.ContentManifest? CapturedManifest => _captured?.Manifest;
    public string? GenerationId => _captured?.GenerationId;
    public string? Namespace => _captured?.Namespace;
    public Karpik.Content.Core.AssetTarget? Target => _captured?.Target;
    public void Retain() => _captured?.Retain();
    public void Release() => _captured?.Release();
    public void Dispose() => _captured?.Dispose();

    public ReadOnlyMemory<byte> Get(string locator)
    {
        if (_captured != null) return _captured.Get(locator);
        string combined = Path.Combine(_root, locator.Replace('/', fileSystem.DirectorySeparatorChar));
        string fullPath = Path.GetFullPath(combined);
        if (!PathSafety.IsContained(_root, fullPath))
        {
            throw new InvalidDataException($"KCR201 Path traversal detected: locator '{locator}' escapes root '{_root}'");
        }

        if (!fileSystem.Exists(fullPath))
        {
            throw new InvalidDataException($"KCR201 Missing artifact {locator}");
        }

        using Stream input = fileSystem.OpenRead(fullPath);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) =>
        _captured != null ? _captured.GetAsync(locator, ct) : Task.Run(() => Get(locator), ct);
}

internal static class PathSafety
{
    public static bool IsContained(string root, string candidate)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullCandidate = Path.GetFullPath(candidate);
        return string.Equals(fullRoot, fullCandidate, PathComparison) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
