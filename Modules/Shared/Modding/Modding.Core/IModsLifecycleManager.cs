using Karpik.Jobs;

namespace Karpik.Engine.Shared.Modding;

public interface IModsLifecycleManager
{
    public IReadOnlyList<ModContainer> Containers { get; }
    
    public JobHandle Load(IReadOnlyList<ModDefinition> definitions, ExecutionSide side);
}