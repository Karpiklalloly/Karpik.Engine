using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Physics.Core;

[Module]
public class Physics2DInstaller : IModule, IModuleConfiguratable
{
    public string Name => "Physics2D.Core";
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
        container.Register<IEcsModule>(new Physics2DModule());
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}