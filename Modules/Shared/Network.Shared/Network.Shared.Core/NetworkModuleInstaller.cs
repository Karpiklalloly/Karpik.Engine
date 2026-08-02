using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.LiteNetLib.Configs;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

[Module]
public class NetworkModuleInstaller : IModuleInstaller
{
    public string Name => "Network.Shared.Core";
 
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(new NetworkConfig());
    }
}