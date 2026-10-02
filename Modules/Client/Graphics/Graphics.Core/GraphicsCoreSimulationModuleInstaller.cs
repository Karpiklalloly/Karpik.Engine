using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

[Module(ModuleScope.Simulation, -101)]
public class GraphicsCoreSimulationModuleInstaller : IModuleInstaller
{
    public string Name => "Graphics.Core";

    public IModule? CreateModule() => new GraphicsCoreModule();
}