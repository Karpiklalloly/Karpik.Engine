using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Core;

/// <summary>Declares system types and pipeline configuration before creating the Simulation scope.</summary>
public interface ISystemRegistry
{
    /// <summary>Declares a DI-created type. RuntimeSystem types must use ISystemRuntimeRegistry.AddInstance instead.</summary>
    public void Add<T>(string layer = "BASIC_LAYER", int order = 0) where T : class, ISystem;
    public void ConfigurePipeline(Action<IBuilder> configure);
}
