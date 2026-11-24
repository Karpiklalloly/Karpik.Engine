namespace Karpik.Engine.Shared.Modding.Bindings;

public interface IComponentField<TValue> where TValue : unmanaged
{
    TValue Read(int entityId);
    void Write(int entityId, TValue value);
}
