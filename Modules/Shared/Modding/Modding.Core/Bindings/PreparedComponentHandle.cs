namespace Karpik.Engine.Shared.Modding.Bindings;

public sealed class PreparedComponentHandle
{
    private readonly ComponentCallbackScope _scope;
    private readonly string _owner;
    private readonly PreparedComponentView _view;
    internal IComponentPoolAccess Pool { get; }
    internal ComponentCallbackScope SimulationScope => _scope;
    public string ComponentId { get; }
    public Type? ClrType { get; }
    public bool Readonly { get; }
    // Public descriptors must not hand out column access that bypasses this domain.
    public ReadOnlyMemory<ComponentFieldDescriptor> Fields { get; }
    internal ReadOnlyMemory<ComponentFieldDescriptor> DirectFields => Pool.Fields;

    internal PreparedComponentHandle(string id, Type? clrType, bool readOnly, IComponentPoolAccess pool, ComponentCallbackScope scope, string owner)
    {
        ComponentId = id; ClrType = clrType; Readonly = readOnly; Pool = pool; _scope = scope; _owner = owner;
        _view = new PreparedComponentView(this);
        var source = pool.Fields.Span;
        var fields = new ComponentFieldDescriptor[source.Length];
        for (int i = 0; i < fields.Length; i++) fields[i] = source[i].BindTo(this);
        Fields = fields;
    }

    public bool Has(int entityId) { Require(); return Pool.Has(entityId); }
    public PreparedComponentView Get(int entityId)
    {
        if (!Has(entityId)) throw new InvalidOperationException($"Component '{ComponentId}' does not exist.");
        _view.EntityId = entityId; return _view;
    }
    public PreparedComponentView Add(int entityId)
    {
        RequireMutable(); Pool.Add(entityId); _view.EntityId = entityId; return _view;
    }
    public void Del(int entityId) { RequireMutable(); Pool.Del(entityId); }
    public ComponentSnapshot NewSnapshot()
    {
        Require();
        return new ComponentSnapshot(ComponentId, ClrType, _scope, DirectFields.Span);
    }
    public ComponentSnapshot Copy(int entityId)
    {
        if (!Has(entityId)) throw new InvalidOperationException($"Component '{ComponentId}' does not exist.");
        var snapshot = NewSnapshot();
        snapshot.CopyFrom(this, entityId);
        return snapshot;
    }
    public void CopyTo(int entityId, ComponentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.CopyFrom(this, entityId);
    }
    internal void Require() => _scope.Require(_owner);
    internal void RequireMutable()
    {
        Require(); if (Readonly) throw new InvalidOperationException($"Component '{ComponentId}' is readonly.");
    }
}

public sealed class PreparedComponentView : IComponentDataView
{
    private readonly PreparedComponentHandle _handle;
    internal int EntityId;
    internal PreparedComponentView(PreparedComponentHandle handle) => _handle = handle;
    public ReadOnlyMemory<ComponentFieldDescriptor> Fields => _handle.Fields;
    public void ValidateAccess()
    {
        if (!_handle.Has(EntityId)) throw new InvalidOperationException($"Component '{_handle.ComponentId}' does not exist.");
    }
    public TValue Read<TValue>(int field) where TValue : unmanaged => ReadField(field).Get<TValue>().Read(EntityId);
    public void Write<TValue>(int field, TValue value) where TValue : unmanaged => WriteField(field).Get<TValue>().Write(EntityId, value);
    public ComponentPointerValue ReadPointer(int field) => Read<ComponentPointerValue>(field);
    public void WritePointer(int field, ComponentPointerValue value) => Write(field, value);
    public SchemaScalarKind GetFieldKind(int field) => ReadField(field).Kind;
    public bool ReadBool(int field) => ReadField(field).Bool.Read(EntityId);
    public int ReadI32(int field) => ReadField(field).I32.Read(EntityId);
    public uint ReadU32(int field) => ReadField(field).U32.Read(EntityId);
    public float ReadF32(int field) => ReadField(field).F32.Read(EntityId);
    public double ReadF64(int field) => ReadField(field).F64.Read(EntityId);
    public sbyte ReadI8(int field) => ReadField(field).I8.Read(EntityId);
    public byte ReadU8(int field) => ReadField(field).U8.Read(EntityId);
    public short ReadI16(int field) => ReadField(field).I16.Read(EntityId);
    public ushort ReadU16(int field) => ReadField(field).U16.Read(EntityId);
    public char ReadChar(int field) => ReadField(field).Char.Read(EntityId);
    public long ReadI64(int field) => ReadField(field).I64.Read(EntityId);
    public ulong ReadU64(int field) => ReadField(field).U64.Read(EntityId);
    public decimal ReadDecimal(int field) => ReadField(field).Decimal.Read(EntityId);
    public nint ReadNInt(int field) => ReadField(field).NInt.Read(EntityId);
    public nuint ReadNUInt(int field) => ReadField(field).NUInt.Read(EntityId);
    public void WriteBool(int field, bool value) => WriteField(field).Bool.Write(EntityId, value);
    public void WriteI32(int field, int value) => WriteField(field).I32.Write(EntityId, value);
    public void WriteU32(int field, uint value) => WriteField(field).U32.Write(EntityId, value);
    public void WriteF32(int field, float value) => WriteField(field).F32.Write(EntityId, value);
    public void WriteF64(int field, double value) => WriteField(field).F64.Write(EntityId, value);
    public void WriteI8(int field, sbyte value) => WriteField(field).I8.Write(EntityId, value);
    public void WriteU8(int field, byte value) => WriteField(field).U8.Write(EntityId, value);
    public void WriteI16(int field, short value) => WriteField(field).I16.Write(EntityId, value);
    public void WriteU16(int field, ushort value) => WriteField(field).U16.Write(EntityId, value);
    public void WriteChar(int field, char value) => WriteField(field).Char.Write(EntityId, value);
    public void WriteI64(int field, long value) => WriteField(field).I64.Write(EntityId, value);
    public void WriteU64(int field, ulong value) => WriteField(field).U64.Write(EntityId, value);
    public void WriteDecimal(int field, decimal value) => WriteField(field).Decimal.Write(EntityId, value);
    public void WriteNInt(int field, nint value) => WriteField(field).NInt.Write(EntityId, value);
    public void WriteNUInt(int field, nuint value) => WriteField(field).NUInt.Write(EntityId, value);
    private ComponentFieldDescriptor ReadField(int field)
    {
        _handle.Require(); return _handle.DirectFields.Span[field];
    }
    private ComponentFieldDescriptor WriteField(int field)
    {
        _handle.RequireMutable();
        ComponentFieldDescriptor descriptor = _handle.DirectFields.Span[field];
        if (!descriptor.CanWrite) throw new InvalidOperationException($"Field '{descriptor.Name}' is readonly.");
        return descriptor;
    }
}
