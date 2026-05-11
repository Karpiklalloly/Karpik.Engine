using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Network.Server.LiteNetLib.Systems;

namespace Network.Server.LiteNetLib;

internal class NetworkServerModule : IModule
{
    public void Import(IBuilder b)
    {
        b.Add(new InitNetworkClientSystem());
        b.Add(new UpdateNetworkClientSystem(), CustomLayers.BEGIN_PROGRAM_LAYER);
        b.Add(new DestroyNetworkClientSystem());
    }
}