namespace Karpik.Content.Runtime;

/// <summary>Cold-path retention of a captured store. No filesystem work belongs in registry lookup or commit.</summary>
public interface IContentStoreLifetime
{
    void Retain();
    void Release();
}

internal static class ContentStoreLifetime
{
    internal static IContentStore Retain(IContentStore store)
    {
        if (store is IContentStoreLifetime lifetime) lifetime.Retain();
        return store;
    }
    internal static void Release(IContentStore store)
    {
        if (store is IContentStoreLifetime lifetime) lifetime.Release();
    }
}
