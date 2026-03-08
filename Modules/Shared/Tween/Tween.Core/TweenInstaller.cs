using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Tweening;

[Module]
public class TweenInstaller : IModule, IModuleConfiguratable
{
    public string Name => "Tween.Core";
    
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(new Tween());
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
        container.Register<IEcsModule>(new TweenModule());
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}