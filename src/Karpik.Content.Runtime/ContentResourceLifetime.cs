namespace Karpik.Content.Runtime;

/// <summary>CPU ownership of a backend resource bundle. Destruction is driven only by cold control work.</summary>
public sealed class ContentResourceLifetime
{
    private const int Retired = 1 << 30;
    private const int Disposed = int.MinValue;
    private int _state;
    private readonly Action _destroy;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion => _completion.Task;
    public ContentResourceLifetime(Action destroy)
    {
        _destroy = destroy ?? throw new ArgumentNullException(nameof(destroy));
        IdentityHash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public int IdentityHash { get; }
    public bool TryRetain()
    {
        int state = Volatile.Read(ref _state);
        while (state >= 0 && (state & Retired) == 0)
        {
            if (state == Retired - 1) return false;
            int observed = Interlocked.CompareExchange(ref _state, state + 1, state);
            if (observed == state) return true;
            state = observed;
        }
        return false;
    }
    /// <summary>Transfer an existing live CPU claim, including after retirement. Caller must already own a claim.</summary>
    public void RetainExisting()
    {
        int state = Volatile.Read(ref _state);
        while (state >= 0 && (state & (Retired - 1)) != 0 && (state & (Retired - 1)) != Retired - 1)
        {
            int observed = Interlocked.CompareExchange(ref _state, state + 1, state);
            if (observed == state) return;
            state = observed;
        }
        throw new InvalidOperationException("Transfer requires an existing live CPU claim.");
    }
    public void Release() => Interlocked.Decrement(ref _state);
    public void Retire()
    {
        int state = Volatile.Read(ref _state);
        while (state >= 0 && (state & Retired) == 0)
        {
            int observed = Interlocked.CompareExchange(ref _state, state | Retired, state);
            if (observed == state) return;
            state = observed;
        }
    }
    public bool TryDispose()
    {
        if (Interlocked.CompareExchange(ref _state, Disposed, Retired) != Retired) return false;
        try { _destroy(); _completion.TrySetResult(); }
        catch (Exception ex) { _completion.TrySetException(ex); throw; }
        return true;
    }
    public async Task DrainAsync()
    {
        Retire();
        while (Volatile.Read(ref _state) != Disposed)
        {
            if (TryDispose()) break;
            await Task.Delay(1).ConfigureAwait(false);
        }
        await Completion.ConfigureAwait(false);
    }
}

public interface IContentTrackedResource
{
    ContentResourceLifetime? Lifetime { get; }
}

/// <summary>Stable, engine-scoped decoders; never register loaders from reloadable gameplay assemblies.</summary>
public interface IContentPayloadLoader
{
    bool CanLoad(Type payloadType, string declaredType);
    ValueTask<object> LoadAsync(Type payloadType, string declaredType, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
}
