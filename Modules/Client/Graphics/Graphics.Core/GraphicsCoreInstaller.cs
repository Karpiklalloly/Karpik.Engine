using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

[Module]
public class GraphicsCoreInstaller : IModule, IModuleConfiguratable
{
    public string Name => "Graphics.Core";
    
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}