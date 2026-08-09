using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Network.LiteNetLib;

[Module(ModuleScope.Simulation)]
public class NetworkClientModuleInstaller : IModuleInstaller
{
    public string Name => "Network.Client.LiteNetLib";
    
    public IModule? CreateModule() => new NetworkClientModule();
}