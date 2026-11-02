using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

/// <summary>Cold filesystem store whose immutable bytes survive publication until every captured owner releases them.</summary>
public sealed class GenerationContentStore : IContentStore, IContentStoreLifetime, IDisposable
{
    private readonly ContentGeneration _generation;
    private int _references = 1;
    private int _disposed;
    private GenerationContentStore(ContentGeneration generation) => _generation = generation;
    public bool IdentityKnown => _generation.IdentityKnown;
    public static GenerationContentStore OpenOrImportCurrent(string outputRoot) => new(ContentGeneration.OpenOrImportCurrent(outputRoot));
    public string GenerationId => _generation.GenerationId;
    public string Namespace => _generation.Namespace;
    public AssetTarget Target => _generation.Target;
    public ContentManifest Manifest => _generation.Manifest;
    public static GenerationContentStore OpenCurrent(string outputRoot) => new(ContentGeneration.OpenCurrent(outputRoot));
    public static GenerationContentStore Open(string outputRoot, string generationId) => new(ContentGeneration.Open(outputRoot, generationId));

    public void Retain()
    {
        while (true)
        {
            int count = Volatile.Read(ref _references);
            ObjectDisposedException.ThrowIf(count <= 0, this);
            if (count == int.MaxValue) throw new InvalidOperationException("Content store owner count overflow.");
            if (Interlocked.CompareExchange(ref _references, count + 1, count) == count) return;
        }
    }
    public void Release()
    {
        int remaining = Interlocked.Decrement(ref _references);
        if (remaining == 0) _generation.Dispose();
        if (remaining < 0) throw new InvalidOperationException("Content store ownership was released more than retained.");
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) Release();
    }
    public ReadOnlyMemory<byte> Get(string locator)
    {
        Retain();
        try
        {
            string path = Path.GetFullPath(Path.Combine(_generation.RootPath, locator.Replace('/', Path.DirectorySeparatorChar)));
            if (!PathSafety.IsContained(_generation.RootPath, path)) throw new InvalidDataException("KCR201 Content locator escapes captured generation.");
            return File.ReadAllBytes(path);
        }
        finally { Release(); }
    }
    public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default)
    {
        // Hold before queueing so disposal cannot release the captured bytes before the worker starts.
        Retain();
        return ReadAsync(locator, ct);
    }
    private async Task<ReadOnlyMemory<byte>> ReadAsync(string locator, CancellationToken ct)
    {
        try { return await Task.Run(() => Get(locator), ct).ConfigureAwait(false); }
        finally { Release(); }
    }
}
