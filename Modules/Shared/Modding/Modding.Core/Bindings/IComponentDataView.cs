namespace Karpik.Engine.Shared.Modding.Bindings;

/// <summary>Typed access to live or detached component data. No scalar values are boxed.</summary>
public interface IComponentDataView
{
    ReadOnlyMemory<ComponentFieldDescriptor> Fields { get; }
    SchemaScalarKind GetFieldKind(int field);
    TValue Read<TValue>(int field) where TValue : unmanaged;
    void Write<TValue>(int field, TValue value) where TValue : unmanaged;
    void ValidateAccess();
}
