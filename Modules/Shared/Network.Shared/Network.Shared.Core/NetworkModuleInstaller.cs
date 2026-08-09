using Autofac;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

[Module(ModuleScope.Simulation)]
public class NetworkModuleInstaller : IModuleInstaller
{
    public string Name => "Network.Shared.Core";
 
    public void OnRegisterServices(ContainerBuilder builder)
    {
    }
}