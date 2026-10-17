namespace Karpik.Engine.Shared.ECS.Scheduling;

public interface IEcsRenderPrepareRegistryProvider
{
    ReadOnlySpan<EcsUpdateSystemDescriptor> GetRenderPrepareSystems();
}
