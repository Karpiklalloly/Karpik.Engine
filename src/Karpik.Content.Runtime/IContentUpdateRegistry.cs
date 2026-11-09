using Karpik.Content.Core;
namespace Karpik.Content.Runtime;
public interface IContentUpdateRegistry
{
    bool TryResolve<T>(AssetId id, out AssetRef<T> reference);
    Task<PreparedContentUpdate> PrepareAsync(ContentManifest manifest, CancellationToken cancellationToken = default);
    Task<PreparedContentUpdate> PrepareAsync(ContentManifest manifest, IContentStore store, CancellationToken cancellationToken = default);
    bool TryCommit(PreparedContentUpdate update);
}

/// <summary>One-shot, registry-bound update. Dispose aborts an unpublished candidate; Completion drains retired CPU owners.</summary>
public sealed class PreparedContentUpdate : IDisposable, IThreadPoolWorkItem
{
    internal readonly ContentRegistry Owner;
    internal readonly ContentSnapshot Basis;
    internal readonly ContentSnapshot Candidate;
    internal readonly ContentSlot[] Replaced;
    internal readonly ContentSlot[] Created;
    internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Committed;
    internal int State;
    public Task Completion => Finished.Task;
    internal PreparedContentUpdate(ContentRegistry owner, ContentSnapshot basis, ContentSnapshot candidate,
        ContentSlot[] replaced, ContentSlot[] created)
    {
        Owner = owner; Basis = basis; Candidate = candidate; Replaced = replaced; Created = created;
    }
    void IThreadPoolWorkItem.Execute() => Owner.CompleteUpdate(this);
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref State, 2, 0) == 0) ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }
}
