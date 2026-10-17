using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;

namespace Karpik.Content.Runtime;

[Export(typeof(IContentStore))]
[Export(typeof(FileContentStore))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class FileContentStore(IFileSystem fileSystem) : IContentStore
{
    private readonly string _root = Path.GetFullPath(fileSystem.ContentPath);

    public ReadOnlyMemory<byte> Get(string locator)
    {
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

    public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Task.Run(() => Get(locator), ct);
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
