using Autofac;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

[Module(ModuleScope.Simulation)]
public class LiteNetLibNetworkModuleInstaller : IModuleInstaller
{
    public string Name => "Network.Shared.LiteNetLib";
}