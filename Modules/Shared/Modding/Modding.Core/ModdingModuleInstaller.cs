using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Shared.Modding;

[Module]
public class ModdingModuleInstaller : IModuleInstaller
{
    public string Name => "Modding.Core";

    public void OnRegisterServices(ContainerBuilder builder)
    {
        
    }
}