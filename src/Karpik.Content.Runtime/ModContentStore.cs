using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

internal sealed class ModContentStore(string root, long bytes) : IContentStore, IContentStoreLifetime
{
    private int _references = 1;

    public void Retain()
    {
        while (true)
        {
            int count = Volatile.Read(ref _references);
            ObjectDisposedException.ThrowIf(count <= 0, this);
            if (Interlocked.CompareExchange(ref _references, checked(count + 1), count) == count) return;
        }
    }

    public void Release()
    {
        int remaining = Interlocked.Decrement(ref _references);
        if (remaining < 0) throw new InvalidOperationException("Mod Content store ownership was released more than retained.");
        if (remaining == 0) ModContentCapture.DeleteOwnedCapture(root, bytes);
    }

    public ReadOnlyMemory<byte> Get(string artifactLocator)
    {
        Retain();
        try { return File.ReadAllBytes(PathFor(artifactLocator)); }
        finally { Release(); }
    }

    public async Task<ReadOnlyMemory<byte>> GetAsync(string artifactLocator, CancellationToken ct = default)
    {
        Retain();
        try { return await File.ReadAllBytesAsync(PathFor(artifactLocator), ct).ConfigureAwait(false); }
        finally { Release(); }
    }

    private string PathFor(string locator) => ContentCaptureFiles.ResolveArtifact(root, locator);
}
