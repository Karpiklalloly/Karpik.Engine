using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Core;

public interface ISystemRegistry
{
    public void Add<T>(string layer = "BASIC_LAYER", int order = 0) where T : class, ISystem;
    public void AddInstance(string systemId, ISystem system, string layer = "BASIC_LAYER", int order = 0);
    /// <summary>Registers an Update system with per-instance metadata and a nonempty descriptor SystemId.</summary>
    public void AddInstance(ISystem system, EcsUpdateSystemDescriptor descriptor, string layer = "BASIC_LAYER", int order = 0);
    public void ConfigurePipeline(Action<IBuilder> configure);
}
