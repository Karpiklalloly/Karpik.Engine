using DCFApixels.DragonECS;
using Karpik.Engine.Client.Network.LiteNetLib.Systems;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Network.LiteNetLib;

internal class NetworkClientModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new InitNetworkClientSystem())
            .Add(new UpdateNetworkClientSystem(), CustomLayers.BEGIN_PROGRAM_LAYER);
    }
}