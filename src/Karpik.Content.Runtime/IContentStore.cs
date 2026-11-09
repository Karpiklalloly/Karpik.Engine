namespace Karpik.Content.Runtime;

public interface IContentStore
{
    /// <summary>Opens a stream that owns its bytes or file handle until disposed.</summary>
    Stream OpenRead(string artifactLocator) => new MemoryStream(Get(artifactLocator).ToArray(), writable: false);
    ReadOnlyMemory<byte> Get(string artifactLocator);
    Task<ReadOnlyMemory<byte>> GetAsync(string artifactLocator, CancellationToken ct = default);
}
