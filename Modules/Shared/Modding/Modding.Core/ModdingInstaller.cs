using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding;

[Module]
public class ModdingInstaller : IModule, IModuleConfiguratable
{
    public string Name => "Modding.Core";

    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}