using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Shared.Tweening;

[Module]
public class TweenModuleInstaller : IModuleInstaller, IModuleInstallerConfiguratable
{
    public string Name => "Tween.Core";
    
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(new Tween());
    }

    public void OnConfigure(IServiceResolver services, out IModule? module)
    {
        module = new TweenModule();
    }

    public void OnConfigureComplete(IServiceResolver services)
    {
        
    }
}