using DCFApixels.DragonECS;
namespace Karpik.Engine.Shared.Modding.Bindings;

internal sealed class DynamicComponentPool : IComponentPoolAccess, ICommandComponentPool
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
        ComponentLeaf[] leaves = ComponentLayout.Expand(schema);
        var columns = new Dictionary<string, IColumn>(StringComparer.Ordinal);
        var fields = new ComponentFieldDescriptor[leaves.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            ComponentLeaf leaf = leaves[i];
            fields[i] = leaf.Kind switch
            {
                SchemaScalarKind.Bool => CreateField<bool>(leaf, capacity, columns),
                SchemaScalarKind.I8 => CreateField<sbyte>(leaf, capacity, columns),
                SchemaScalarKind.U8 => CreateField<byte>(leaf, capacity, columns),
                SchemaScalarKind.I16 => CreateField<short>(leaf, capacity, columns),
                SchemaScalarKind.U16 => CreateField<ushort>(leaf, capacity, columns),
                SchemaScalarKind.Char => CreateField<char>(leaf, capacity, columns),
                SchemaScalarKind.I32 => CreateField<int>(leaf, capacity, columns),
                SchemaScalarKind.U32 => CreateField<uint>(leaf, capacity, columns),
                SchemaScalarKind.F32 => CreateField<float>(leaf, capacity, columns),
                SchemaScalarKind.F64 => CreateField<double>(leaf, capacity, columns),
                SchemaScalarKind.I64 => CreateField<long>(leaf, capacity, columns),
                SchemaScalarKind.U64 => CreateField<ulong>(leaf, capacity, columns),
                SchemaScalarKind.Decimal => CreateField<decimal>(leaf, capacity, columns),
                SchemaScalarKind.NInt => CreateField<nint>(leaf, capacity, columns),
                SchemaScalarKind.NUInt => CreateField<nuint>(leaf, capacity, columns),
                SchemaScalarKind.Pointer => CreateField<ComponentPointerValue>(leaf, capacity, columns),
                _ => throw new InvalidDataException($"Unsupported field kind in schema '{schema.Id}'.")
            };
        }
        _columns = columns.Values.ToArray();
        Fields = fields;
    }

    public int Count { get { _storage.ThrowIfDisposed(); return _count; } }
    public ReadOnlyMemory<ComponentFieldDescriptor> Fields { get; }
    EcsDefaultWorld ICommandComponentPool.CommandWorld => _storage.CommandWorld;
    int ICommandComponentPool.CommandCapacity => _entities.Length;
    bool ICommandComponentPool.IsDynamic => true;
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

    private ComponentFieldDescriptor CreateField<T>(ComponentLeaf leaf, int capacity, Dictionary<string, IColumn> columns) where T : unmanaged
    {
        ComponentPointerType pointerType = default;
        ComponentPointerRegistry? pointers = null;
        if (leaf.Kind == SchemaScalarKind.Pointer)
        {
            pointers = _storage.Pointers;
            pointerType = pointers.ResolveType(leaf.PointerTypeId!);
        }
        if (!columns.TryGetValue(leaf.ColumnKey, out var existing))
        {
            existing = new Column<T>(this, capacity, leaf.Width, pointers, pointerType);
            columns.Add(leaf.ColumnKey, existing);
        }
        var column = (Column<T>)existing;
        IComponentField<T> accessor = leaf.Width == 1 ? column : new OffsetField<T>(column, leaf.Offset);
        return new ComponentFieldDescriptor(leaf.Name, true, leaf.Kind, accessor)
        {
            Path = leaf.Path, PointerRegistry = pointers, PointerType = pointerType
        };
    }

    private interface IColumn
    {
        void Resize(int capacity);
        void Reset(int index);
        void Move(int from, int to);
        void Clear();
    }

    private sealed class Column<T>(DynamicComponentPool owner, int capacity, int width,
        ComponentPointerRegistry? pointers, ComponentPointerType pointerType) : IColumn, IComponentField<T> where T : unmanaged
    {
        private T[] _values = new T[checked(capacity * width)];
        public T Read(int entityId) => Read(entityId, 0);
        public void Write(int entityId, T value) => Write(entityId, 0, value);
        internal T Read(int entityId, int offset)
        {
            owner.ValidatePresent(entityId);
            T value = _values[checked(owner._mapping[entityId] * width + offset)];
            Validate(value); return value;
        }
        internal void Write(int entityId, int offset, T value)
        {
            owner.ValidatePresent(entityId); Validate(value);
            _values[checked(owner._mapping[entityId] * width + offset)] = value;
        }
        private void Validate(T value)
        {
            if (pointers is not null)
                pointers.ValidateAssignment(System.Runtime.CompilerServices.Unsafe.As<T, ComponentPointerValue>(ref value), pointerType);
        }
        public void Resize(int capacity) => Array.Resize(ref _values, checked(capacity * width));
        public void Reset(int index) => _values.AsSpan(checked(index * width), width).Clear();
        public void Move(int from, int to)
        {
            _values.AsSpan(checked(from * width), width).CopyTo(_values.AsSpan(checked(to * width), width));
            Reset(from);
        }
        public void Clear() => _values = [];
    }

    private sealed class OffsetField<T>(Column<T> column, int offset) : IComponentField<T> where T : unmanaged
    {
        public T Read(int entityId) => column.Read(entityId, offset);
        public void Write(int entityId, T value) => column.Write(entityId, offset, value);
    }
}
