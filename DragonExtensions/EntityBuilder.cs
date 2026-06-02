namespace DragonExtensions;

public ref struct EntityBuilder(EcsWorld world) : IDisposable
{
    private int _entityId = -1;
    private List<(Type Type, object Component)> _buffer = new();

    public EntityBuilder WithId(int id)
    {
        _entityId = id;
        return this;
    }

    public EntityBuilder Add<T>(in T component) where T : struct
    {
        _buffer.Add((typeof(T), component));
        return this;
    }

    private entlong Commit()
    {
        lock (world)
        {
            int id = _entityId >= 0 ? world.NewEntity(_entityId) : world.NewEntity();

            foreach (var (type, component) in _buffer)
            {
                world.FindPoolInstance(type).AddRaw(id, component);
            }

            _buffer.Clear();
            _entityId = -1;
            return world.GetEntityLong(id);
        }
    }

    public void Dispose()
    {
        Commit();
    }
}
