using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding;

[Export(typeof(ScriptSystemOptions))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class ScriptSystemOptions
{
    public ScriptSystemErrorPolicy ErrorPolicy { get; init; } = ScriptSystemErrorPolicy.StopSimulation;
}
