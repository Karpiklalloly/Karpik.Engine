using Karpik.Engine.Core;
using Network.Server.LiteNetLib.Systems;

namespace Network.Server.LiteNetLib;

internal class NetworkServerModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<InitNetworkClientSystem>();
        systems.Add<UpdateNetworkClientSystem>(CustomLayers.BEGIN_PROGRAM_LAYER);
        systems.Add<DestroyNetworkClientSystem>();
    }
}