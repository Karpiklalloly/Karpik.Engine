namespace Karpik.Engine.Shared.Modding.Bindings;

internal sealed class DynamicF32ComponentPool : IComponentPoolAccess
{
    private readonly DynamicComponentStorage _storage;
    private int[] _mapping;
    private int[] _entities;
    private float[][] _columns;
    private int _count;

    internal DynamicF32ComponentPool(DynamicComponentStorage storage, ComponentSchema schema, int worldCapacity, int initialCapacity)
    {
        _storage = storage;
        _mapping = new int[worldCapacity]; Array.Fill(_mapping, -1);
        int capacity = 1;
        while (capacity < initialCapacity) capacity = checked(capacity * 2);
        _entities = new int[capacity];
        _columns = new float[schema.Fields.Length][];
        var fields = new ComponentFieldDescriptor[schema.Fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            _columns[i] = new float[capacity];
            fields[i] = new ComponentFieldDescriptor(schema.Fields[i].Name, true, new Field(this, i));
        }
        Fields = fields;
    }

    public int Count { get { _storage.ThrowIfDisposed(); return _count; } }
    public ReadOnlyMemory<ComponentFieldDescriptor> Fields { get; }
    public bool Has(int entityId) { _storage.ValidateEntity(entityId); return _mapping[entityId] >= 0; }

    public void Add(int entityId)
    {
        if (Has(entityId)) throw new InvalidOperationException("Component already exists.");
        if (_count == _entities.Length)
        {
            int capacity = checked(_entities.Length * 2);
            Array.Resize(ref _entities, capacity);
            for (int i = 0; i < _columns.Length; i++) Array.Resize(ref _columns[i], capacity);
        }
        _storage.Added(entityId);
        _entities[_count] = entityId;
        _mapping[entityId] = _count;
        foreach (float[] column in _columns) column[_count] = 0;
        _count++;
    }

    public void Del(int entityId)
    {
        ValidatePresent(entityId);
        RemoveReleasedEntity(entityId);
        _storage.Removed(entityId);
    }

    internal void RemoveReleasedEntity(int entityId)
    {
        int index = _mapping[entityId];
        if (index < 0) return;
        int last = --_count;
        int moved = _entities[last];
        _entities[index] = moved;
        _mapping[moved] = index;
        foreach (float[] column in _columns) { column[index] = column[last]; column[last] = 0; }
        _entities[last] = 0;
        _mapping[entityId] = -1;
    }

    public int CopyEntityIds(Span<int> destination)
    {
        _storage.ThrowIfDisposed();
        _entities.AsSpan(0, _count).CopyTo(destination);
        return _count;
    }

    internal void ResizeMapping(int newSize)
    {
        int previous = _mapping.Length;
        Array.Resize(ref _mapping, newSize);
        Array.Fill(_mapping, -1, previous, newSize - previous);
    }

    internal void Clear() { _mapping = []; _entities = []; _columns = []; _count = 0; }

    private void ValidatePresent(int entityId)
    {
        if (!Has(entityId)) throw new InvalidOperationException("Component does not exist.");
    }

    private sealed class Field(DynamicF32ComponentPool owner, int column) : IComponentField<float>
    {
        public float Read(int entityId) { owner.ValidatePresent(entityId); return owner._columns[column][owner._mapping[entityId]]; }
        public void Write(int entityId, float value) { owner.ValidatePresent(entityId); owner._columns[column][owner._mapping[entityId]] = value; }
    }
}
