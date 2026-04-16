using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

[Module(-101)]
public class GraphicsCoreInstaller : IModule, IModuleConfiguratable, IModuleDestroy
{
    public string Name => "Graphics.Core";
    
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
        
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
    
    public void Destroy()
    {
    }
}
