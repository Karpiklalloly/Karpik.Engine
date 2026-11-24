namespace Karpik.Engine.Shared.Modding.Bindings;

/// <summary>
/// Detached component data owned by one Simulation. It neither retains the source entity/pool
/// nor writes changes back to ECS. Creation allocates; copying into a prepared buffer does not.
/// </summary>
public sealed class ComponentSnapshot : IComponentDataView
{
    private readonly ComponentCallbackScope _simulation;
    private readonly ComponentFieldDescriptor[] _fields;
    private readonly ISnapshotField[] _storage;

    internal ComponentCallbackScope SimulationScope => _simulation;
    public string ComponentId { get; }
    public Type? ClrType { get; }
    public ReadOnlyMemory<ComponentFieldDescriptor> Fields
    {
        get { ValidateAccess(); return _fields; }
    }

    internal ComponentSnapshot(string componentId, Type? clrType, ComponentCallbackScope simulation,
        ReadOnlySpan<ComponentFieldDescriptor> sourceMetadata)
    {
        _simulation = simulation;
        ComponentId = componentId;
        ClrType = clrType;
        ValidateAccess();
        _fields = new ComponentFieldDescriptor[sourceMetadata.Length];
        _storage = new ISnapshotField[sourceMetadata.Length];
        for (int i = 0; i < sourceMetadata.Length; i++)
        {
            ComponentFieldDescriptor source = sourceMetadata[i];
            ISnapshotField storage = source.Kind switch
            {
                SchemaScalarKind.Bool => new Field<bool>(this, source.CanWrite),
                SchemaScalarKind.I8 => new Field<sbyte>(this, source.CanWrite),
                SchemaScalarKind.U8 => new Field<byte>(this, source.CanWrite),
                SchemaScalarKind.I16 => new Field<short>(this, source.CanWrite),
                SchemaScalarKind.U16 => new Field<ushort>(this, source.CanWrite),
                SchemaScalarKind.I32 => new Field<int>(this, source.CanWrite),
                SchemaScalarKind.U32 => new Field<uint>(this, source.CanWrite),
                SchemaScalarKind.Char => new Field<char>(this, source.CanWrite),
                SchemaScalarKind.F32 => new Field<float>(this, source.CanWrite),
                SchemaScalarKind.F64 => new Field<double>(this, source.CanWrite),
                SchemaScalarKind.I64 => new Field<long>(this, source.CanWrite),
                SchemaScalarKind.U64 => new Field<ulong>(this, source.CanWrite),
                SchemaScalarKind.Decimal => new Field<decimal>(this, source.CanWrite),
                SchemaScalarKind.NInt => new Field<nint>(this, source.CanWrite),
                SchemaScalarKind.NUInt => new Field<nuint>(this, source.CanWrite),
                SchemaScalarKind.Pointer => new PointerField(this, source.CanWrite,
                    source.PointerRegistry ?? throw new InvalidOperationException("Pointer snapshot fields require host permission."), source.PointerType),
                _ => throw new InvalidOperationException($"Unsupported snapshot field kind '{source.Kind}'.")
            };
            _storage[i] = storage;
            // Rebuild descriptors with snapshot accessors. A record clone would retain source
            // accessors (and therefore native pools/worlds), even after the source was destroyed.
            _fields[i] = new ComponentFieldDescriptor(source.Name, source.CanWrite, source.Kind, storage)
            {
                Path = source.Path.ToArray(),
                PointerRegistry = source.PointerRegistry,
                PointerType = source.PointerType
            };
        }
    }

    public void ValidateAccess() => _simulation.ThrowIfDisposed();
    public SchemaScalarKind GetFieldKind(int field) { ValidateAccess(); return _fields[field].Kind; }
    public TValue Read<TValue>(int field) where TValue : unmanaged
    { ValidateAccess(); return _fields[field].Get<TValue>().Read(0); }
    public void Write<TValue>(int field, TValue value) where TValue : unmanaged
    { ValidateAccess(); _fields[field].Get<TValue>().Write(0, value); }

    public bool ReadBool(int field) => Read<bool>(field);
    public sbyte ReadI8(int field) => Read<sbyte>(field);
    public byte ReadU8(int field) => Read<byte>(field);
    public short ReadI16(int field) => Read<short>(field);
    public ushort ReadU16(int field) => Read<ushort>(field);
    public int ReadI32(int field) => Read<int>(field);
    public uint ReadU32(int field) => Read<uint>(field);
    public char ReadChar(int field) => Read<char>(field);
    public float ReadF32(int field) => Read<float>(field);
    public double ReadF64(int field) => Read<double>(field);
    public long ReadI64(int field) => Read<long>(field);
    public ulong ReadU64(int field) => Read<ulong>(field);
    public decimal ReadDecimal(int field) => Read<decimal>(field);
    public nint ReadNInt(int field) => Read<nint>(field);
    public nuint ReadNUInt(int field) => Read<nuint>(field);
    public ComponentPointerValue ReadPointer(int field) => Read<ComponentPointerValue>(field);
    public void WriteBool(int field, bool value) => Write(field, value);
    public void WriteI8(int field, sbyte value) => Write(field, value);
    public void WriteU8(int field, byte value) => Write(field, value);
    public void WriteI16(int field, short value) => Write(field, value);
    public void WriteU16(int field, ushort value) => Write(field, value);
    public void WriteI32(int field, int value) => Write(field, value);
    public void WriteU32(int field, uint value) => Write(field, value);
    public void WriteChar(int field, char value) => Write(field, value);
    public void WriteF32(int field, float value) => Write(field, value);
    public void WriteF64(int field, double value) => Write(field, value);
    public void WriteI64(int field, long value) => Write(field, value);
    public void WriteU64(int field, ulong value) => Write(field, value);
    public void WriteDecimal(int field, decimal value) => Write(field, value);
    public void WriteNInt(int field, nint value) => Write(field, value);
    public void WriteNUInt(int field, nuint value) => Write(field, value);
    public void WritePointer(int field, ComponentPointerValue value) => Write(field, value);

    internal void CopyFrom(PreparedComponentHandle source, int entity)
    {
        ValidateAccess();
        source.Require();
        if (!ReferenceEquals(source.SimulationScope, _simulation) || source.ComponentId != ComponentId || source.ClrType != ClrType)
            throw new InvalidOperationException("Snapshot buffer belongs to another component or Simulation.");
        ReadOnlySpan<ComponentFieldDescriptor> fields = source.Fields.Span;
        if (fields.Length != _fields.Length) throw LayoutMismatch();
        // Complete compatibility validation before changing any data. Similar layouts are not
        // interchangeable across components, types, domains or frozen schema versions.
        for (int i = 0; i < fields.Length; i++)
        {
            ComponentFieldDescriptor from = fields[i], to = _fields[i];
            if (from.Name != to.Name || from.Kind != to.Kind || from.CanWrite != to.CanWrite ||
                !from.Path.Span.SequenceEqual(to.Path.Span) || !ReferenceEquals(from.PointerRegistry, to.PointerRegistry) ||
                from.PointerType.RegistryId != to.PointerType.RegistryId || from.PointerType.Slot != to.PointerType.Slot)
                throw LayoutMismatch();
        }
        if (!source.Pool.Has(entity)) throw new InvalidOperationException($"Component '{ComponentId}' does not exist.");
        for (int i = 0; i < fields.Length; i++) _storage[i].CopyFrom(fields[i], entity);
    }

    private static InvalidOperationException LayoutMismatch() => new("Snapshot buffer has an incompatible field layout.");

    private interface ISnapshotField
    {
        void CopyFrom(ComponentFieldDescriptor source, int entity);
    }

    private sealed class Field<T>(ComponentSnapshot owner, bool canWrite) : IComponentField<T>, ISnapshotField where T : unmanaged
    {
        private T _value;
        public T Read(int entityId) { owner.ValidateAccess(); return _value; }
        public void Write(int entityId, T value)
        {
            owner.ValidateAccess();
            if (!canWrite) throw new InvalidOperationException("Snapshot field is readonly.");
            _value = value;
        }
        // Populating a readonly leaf is allowed internally; public writes still reject it.
        public void CopyFrom(ComponentFieldDescriptor source, int entity) => _value = source.Get<T>().Read(entity);
    }

    private sealed class PointerField : IComponentField<ComponentPointerValue>, ISnapshotField
    {
        private readonly ComponentSnapshot _owner;
        private readonly bool _canWrite;
        private readonly ComponentPointerRegistry _registry;
        private readonly ComponentPointerType _type;
        private ComponentPointerValue _value;
        internal PointerField(ComponentSnapshot owner, bool canWrite, ComponentPointerRegistry registry, ComponentPointerType type)
        {
            _owner = owner; _canWrite = canWrite; _registry = registry; _type = type;
            registry.ValidateAssignment(default, type);
        }
        public ComponentPointerValue Read(int entityId)
        {
            _owner.ValidateAccess(); _registry.ValidateAssignment(_value, _type); return _value;
        }
        public void Write(int entityId, ComponentPointerValue value)
        {
            _owner.ValidateAccess();
            if (!_canWrite) throw new InvalidOperationException("Snapshot field is readonly.");
            _registry.ValidateAssignment(value, _type); _value = value;
        }
        public void CopyFrom(ComponentFieldDescriptor source, int entity)
        {
            ComponentPointerValue value = source.Pointer.Read(entity);
            _registry.ValidateAssignment(value, _type); _value = value;
        }
    }
}
