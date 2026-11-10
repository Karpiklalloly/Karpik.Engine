using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Core;

/// <summary>Registers Simulation-owned instances during preparation, before pipeline construction.</summary>
public interface ISystemRuntimeRegistry
{
    void AddInstance(string systemId, ISystem system, string layer = "BASIC_LAYER", int order = 0);

    /// <summary>Registers an Update system with per-instance metadata and a nonempty descriptor SystemId.</summary>
    void AddInstance(ISystem system, EcsUpdateSystemDescriptor descriptor, string layer = "BASIC_LAYER", int order = 0);
}
