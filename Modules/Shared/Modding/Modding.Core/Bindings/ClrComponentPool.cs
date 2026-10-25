using DCFApixels.DragonECS;

namespace Karpik.Engine.Shared.Modding.Bindings;

public abstract class ClrComponentPool<T> : IComponentPoolAccess where T : unmanaged, IEcsComponent
{
    private readonly EcsDefaultWorld _world;
    private readonly EcsPool<T> _pool;

    protected ClrComponentPool(EcsDefaultWorld world)
    {
        _world = world;
        _pool = world.GetPool<T>();
    }

    public int Count => _pool.Count;
    public abstract ReadOnlyMemory<ComponentFieldDescriptor> Fields { get; }

    public bool Has(int entityId)
    {
        ValidateEntity(entityId);
        return _pool.Has(entityId);
    }

    public void Add(int entityId)
    {
        if (Has(entityId)) throw new InvalidOperationException("Component already exists.");
        _pool.Add(entityId) = default;
    }

    public void Del(int entityId)
    {
        ValidatePresent(entityId);
        _pool.Del(entityId);
    }

    public int CopyEntityIds(Span<int> destination)
    {
        var source = _pool.ToSpan();
        if (destination.Length < source.Count) throw new ArgumentException("Destination is too short.", nameof(destination));
        for (int i = 0; i < source.Count; i++) destination[i] = source[i];
        return source.Count;
    }

    protected ref readonly T ReadComponent(int entityId)
    {
        ValidatePresent(entityId);
        return ref _pool.Read(entityId);
    }

    protected ref T MutableComponent(int entityId)
    {
        ValidatePresent(entityId);
        return ref _pool.Get(entityId);
    }

    private void ValidateEntity(int entityId)
    {
        if ((uint)entityId >= (uint)_world.Capacity || !_world.IsUsed(entityId))
            throw new InvalidOperationException("Entity is not alive in this world.");
    }

    private void ValidatePresent(int entityId)
    {
        if (!Has(entityId)) throw new InvalidOperationException("Component does not exist.");
    }
}
