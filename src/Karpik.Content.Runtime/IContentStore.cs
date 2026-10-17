namespace Karpik.Content.Runtime;

public interface IContentStore
{
    ReadOnlyMemory<byte> Get(string artifactLocator);
    Task<ReadOnlyMemory<byte>> GetAsync(string artifactLocator, CancellationToken ct = default);
}
