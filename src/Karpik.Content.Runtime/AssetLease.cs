namespace Karpik.Content.Runtime;

public readonly ref struct AssetLease<T>
{
    private readonly ContentRegistry _registry;
    private readonly AssetRef<T> _ref;
    private readonly uint _snapshotVersion;

    internal AssetLease(ContentRegistry reg, AssetRef<T> r, uint ver, T payload)
    {
        _registry = reg;
        _ref = r;
        _snapshotVersion = ver;
        Payload = payload;
    }

    public T Payload { get; }

    public bool IsAlive => _registry != null && _registry.IsAlive(_ref) && _snapshotVersion == _ref.Version;

    public void Dispose()
    {
    }
}
