using System.Composition;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding;

[Export(typeof(ScriptSystemOptions))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class ScriptSystemOptions
{
    public StructuralCommandOptions Commands { get; init; } = new();
    public ScriptSystemErrorPolicy ErrorPolicy { get; init; } = ScriptSystemErrorPolicy.StopSimulation;
}
