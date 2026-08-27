using System.IO;

namespace Karpik.Content.Runtime;

public sealed class FileContentStore(string outputRoot) : IContentStore
{
    private readonly string _root = Path.GetFullPath(outputRoot);

    public ReadOnlyMemory<byte> Get(string locator)
    {
        string combined = Path.Combine(_root, locator.Replace('/', Path.DirectorySeparatorChar));
        string fullPath = Path.GetFullPath(combined);
        if (!PathSafety.IsContained(_root, fullPath))
        {
            throw new InvalidDataException($"KCR201 Path traversal detected: locator '{locator}' escapes root '{_root}'");
        }

        if (!File.Exists(fullPath))
        {
            throw new InvalidDataException($"KCR201 Missing artifact {locator}");
        }

        return File.ReadAllBytes(fullPath);
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
