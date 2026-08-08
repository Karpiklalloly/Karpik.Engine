using Autofac;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding;

[Module(ModuleScope.Engine)]
public class ModdingModuleInstaller : IModuleInstaller
{
    public string Name => "Modding.Core";

    public void OnRegisterServices(ContainerBuilder builder)
    {
        
    }
}