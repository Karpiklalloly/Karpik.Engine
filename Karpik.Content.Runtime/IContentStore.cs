namespace Karpik.Content.Runtime;

public interface IContentStore
{
    Stream OpenRead(string artifactLocator);
    ReadOnlyMemory<byte> Get(string artifactLocator);
    Task<ReadOnlyMemory<byte>> GetAsync(string artifactLocator, CancellationToken ct = default);
}
