namespace Karpik.Engine.Shared.ECS.Scheduling;

public interface IEcsUpdateRegistryProvider
{
    ReadOnlySpan<EcsUpdateSystemDescriptor> GetUpdateSystems();
}
