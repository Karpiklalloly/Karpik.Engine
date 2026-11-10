namespace Karpik.Engine.Shared.Modding.Bindings;

internal sealed class DynamicComponentPool : IComponentPoolAccess
{
    private readonly DynamicComponentStorage _storage;
    private int[] _mapping;
    private int[] _entities;
    private IColumn[] _columns;
    private int _count;

    internal DynamicComponentPool(DynamicComponentStorage storage, ComponentSchema schema, int worldCapacity, int initialCapacity)
    {
        _storage = storage;
        _mapping = new int[worldCapacity]; Array.Fill(_mapping, -1);
        int capacity = 1;
        while (capacity < initialCapacity) capacity = checked(capacity * 2);
        _entities = new int[capacity];
        _columns = new IColumn[schema.Fields.Length];
        var fields = new ComponentFieldDescriptor[schema.Fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            string name = schema.Fields[i].Name;
            fields[i] = schema.Fields[i].Kind switch
            {
                SchemaScalarKind.Bool => new(name, true, CreateColumn<bool>(i, capacity)),
                SchemaScalarKind.I32 => new(name, true, CreateColumn<int>(i, capacity)),
                SchemaScalarKind.U32 => new(name, true, CreateColumn<uint>(i, capacity)),
                SchemaScalarKind.F32 => new(name, true, CreateColumn<float>(i, capacity)),
                SchemaScalarKind.F64 => new(name, true, CreateColumn<double>(i, capacity)),
                _ => throw new InvalidDataException($"Unsupported field kind in schema '{schema.Id}'.")
            };
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
            foreach (var column in _columns) column.Resize(capacity);
        }
        _storage.Added(entityId);
        _entities[_count] = entityId;
        _mapping[entityId] = _count;
        foreach (var column in _columns) column.Reset(_count);
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
        foreach (var column in _columns) column.Move(last, index);
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

    internal void Clear()
    {
        foreach (var column in _columns) column.Clear();
        _mapping = []; _entities = []; _columns = []; _count = 0;
    }

    private void ValidatePresent(int entityId)
    {
        if (!Has(entityId)) throw new InvalidOperationException("Component does not exist.");
    }

    private Column<T> CreateColumn<T>(int index, int capacity) where T : unmanaged
    {
        var column = new Column<T>(this, capacity); _columns[index] = column; return column;
    }

    private interface IColumn
    {
        void Resize(int capacity);
        void Reset(int index);
        void Move(int from, int to);
        void Clear();
    }

    private sealed class Column<T>(DynamicComponentPool owner, int capacity) : IColumn, IComponentField<T> where T : unmanaged
    {
        private T[] _values = new T[capacity];
        public T Read(int entityId) { owner.ValidatePresent(entityId); return _values[owner._mapping[entityId]]; }
        public void Write(int entityId, T value) { owner.ValidatePresent(entityId); _values[owner._mapping[entityId]] = value; }
        public void Resize(int capacity) => Array.Resize(ref _values, capacity);
        public void Reset(int index) => _values[index] = default;
        public void Move(int from, int to) { _values[to] = _values[from]; _values[from] = default; }
        public void Clear() => _values = [];
    }
}
