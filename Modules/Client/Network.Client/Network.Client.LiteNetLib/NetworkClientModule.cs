using Karpik.Engine.Client.Network.LiteNetLib.Systems;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Network.LiteNetLib;

internal class NetworkClientModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<InitNetworkClientSystem>();
        systems.Add<DestroyNetworkClientSystem>();
        systems.Add<UpdateNetworkClientSystem>(CustomLayers.BEGIN_PROGRAM_LAYER);
    }
}