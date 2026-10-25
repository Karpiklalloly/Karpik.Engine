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
    public float ReadF32(int field)
    {
        _handle.Require(); return _handle.Fields.Span[field].F32.Read(EntityId);
    }
    public void WriteF32(int field, float value)
    {
        _handle.RequireMutable();
        ComponentFieldDescriptor descriptor = _handle.Fields.Span[field];
        if (!descriptor.CanWrite) throw new InvalidOperationException($"Field '{descriptor.Name}' is readonly.");
        descriptor.F32.Write(EntityId, value);
    }
}
