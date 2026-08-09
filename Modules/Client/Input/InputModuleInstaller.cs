using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

[Module(ModuleScope.Simulation)]
public class InputModuleInstaller : IModuleInstaller
{
    public string Name => "Input";

    public IModule? CreateModule() => new InputModuleEcs();
}
