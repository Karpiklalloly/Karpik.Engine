using Autofac;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.AssetManagement.Core;

[Module(ModuleScope.Engine, -10000)]
public class AssetManagementModuleInstaller : IModuleInstaller
{
    public string Name => "AssetManagement.Core";

    public void OnRegisterServices(ContainerBuilder builder)
    {
    }
}