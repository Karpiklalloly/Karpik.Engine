namespace Karpik.Engine.Shared.Modding.Bindings;

public interface IComponentPoolAccess
{
    int Count { get; }
    ReadOnlyMemory<ComponentFieldDescriptor> Fields { get; }
    bool Has(int entityId);
    void Add(int entityId);
    void Del(int entityId);
    int CopyEntityIds(Span<int> destination);
}
