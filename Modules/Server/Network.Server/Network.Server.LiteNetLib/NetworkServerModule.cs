using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Network.Server.LiteNetLib.Systems;

namespace Network.Server.LiteNetLib;

internal class NetworkServerModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new InitNetworkClientSystem())
            .Add(new UpdateNetworkClientSystem(), CustomLayers.BEGIN_PROGRAM_LAYER)
            .Add(new DestroyNetworkClientSystem());
    }
}