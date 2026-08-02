using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

[Module]
public class LiteNetLibNetworkModuleInstaller : IModuleInstaller
{
    public string Name => "Network.Shared.LiteNetLib";
 
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register<INetworkManager>(new LiteNetLibNetworkManager());
    }
}