using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

[Module]
public class WindowCoreInstaller : IInstaller, IInstallerConfiguratable
{
    public string Name => "Window.Core";
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(new InputCaptureState());
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        module = new WindowCoreModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}
