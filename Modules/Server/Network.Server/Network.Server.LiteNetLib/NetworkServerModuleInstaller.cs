using Karpik.Engine.Core;

namespace Network.Server.LiteNetLib;

[Module(ModuleScope.Simulation)]
public class NetworkServerModuleInstaller : IModuleInstaller
{
    public string Name => "Network.Server.LiteNetLib";

    public IModule? CreateModule() => new NetworkServerModule();
}