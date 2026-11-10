namespace Karpik.Engine.Shared.Modding.Bindings;

public sealed class PreparedComponentHandle
{
    private readonly ComponentCallbackScope _scope;
    private readonly string _owner;
    private readonly PreparedComponentView _view;
    internal IComponentPoolAccess Pool { get; }
    public string ComponentId { get; }
    public Type? ClrType { get; }
    public bool Readonly { get; }
    public ReadOnlyMemory<ComponentFieldDescriptor> Fields => Pool.Fields;

    internal PreparedComponentHandle(string id, Type? clrType, bool readOnly, IComponentPoolAccess pool, ComponentCallbackScope scope, string owner)
    {
        ComponentId = id; ClrType = clrType; Readonly = readOnly; Pool = pool; _scope = scope; _owner = owner;
        _view = new PreparedComponentView(this);
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
    internal void Require() => _scope.Require(_owner);
    internal void RequireMutable()
    {
        Require(); if (Readonly) throw new InvalidOperationException($"Component '{ComponentId}' is readonly.");
    }
}

public sealed class PreparedComponentView
{
    private readonly PreparedComponentHandle _handle;
    internal int EntityId;
    internal PreparedComponentView(PreparedComponentHandle handle) => _handle = handle;
    public SchemaScalarKind GetFieldKind(int field) => ReadField(field).Kind;
    public bool ReadBool(int field) => ReadField(field).Bool.Read(EntityId);
    public int ReadI32(int field) => ReadField(field).I32.Read(EntityId);
    public uint ReadU32(int field) => ReadField(field).U32.Read(EntityId);
    public float ReadF32(int field) => ReadField(field).F32.Read(EntityId);
    public double ReadF64(int field) => ReadField(field).F64.Read(EntityId);
    public void WriteBool(int field, bool value) => WriteField(field).Bool.Write(EntityId, value);
    public void WriteI32(int field, int value) => WriteField(field).I32.Write(EntityId, value);
    public void WriteU32(int field, uint value) => WriteField(field).U32.Write(EntityId, value);
    public void WriteF32(int field, float value) => WriteField(field).F32.Write(EntityId, value);
    public void WriteF64(int field, double value) => WriteField(field).F64.Write(EntityId, value);
    private ComponentFieldDescriptor ReadField(int field)
    {
        _handle.Require(); return _handle.Fields.Span[field];
    }
    private ComponentFieldDescriptor WriteField(int field)
    {
        _handle.RequireMutable();
        ComponentFieldDescriptor descriptor = _handle.Fields.Span[field];
        if (!descriptor.CanWrite) throw new InvalidOperationException($"Field '{descriptor.Name}' is readonly.");
        return descriptor;
    }
}
