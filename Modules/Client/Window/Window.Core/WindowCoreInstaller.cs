using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

[Module]
public class WindowCoreInstaller : IModule, IModuleConfiguratable
{
    public string Name => "Window.Core";
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
        container.Register<IEcsModule>(new WindowCoreModule());
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}