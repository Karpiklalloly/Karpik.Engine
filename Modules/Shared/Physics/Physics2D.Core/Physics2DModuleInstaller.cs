using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Physics.Core;

[Module(ModuleScope.Simulation)]
public class Physics2DModuleInstaller : IModuleInstaller
{
    public string Name => "Physics2D.Core";

    public IModule? CreateModule() => new Physics2DModule();
}