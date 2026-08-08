using Autofac;
using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

[Module(ModuleScope.Engine)]
public class WindowCoreModuleInstaller : IModuleInstaller
{
    public string Name => "Window.Core";
    public void OnRegisterServices(ContainerBuilder builder)
    {
    }

    public IModule? CreateModule() => new WindowCoreModule();
}
