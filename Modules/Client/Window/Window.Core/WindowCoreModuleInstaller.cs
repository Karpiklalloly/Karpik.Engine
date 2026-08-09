using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

[Module(ModuleScope.Engine)]
public class WindowCoreModuleInstaller : IModuleInstaller
{
    public string Name => "Window.Core";

    public IModule? CreateModule() => new WindowCoreModule();
}
