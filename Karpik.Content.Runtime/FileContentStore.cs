using System.IO;

namespace Karpik.Content.Runtime;

public sealed class FileContentStore(string outputRoot) : IContentStore
{
    private readonly string _root = Path.GetFullPath(outputRoot);

    public ReadOnlyMemory<byte> Get(string locator)
    {
        string path = Path.Combine(_root, locator.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Missing artifact {locator}");
        }

        return File.ReadAllBytes(path);
    }

    public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Task.Run(() => Get(locator), ct);
}
