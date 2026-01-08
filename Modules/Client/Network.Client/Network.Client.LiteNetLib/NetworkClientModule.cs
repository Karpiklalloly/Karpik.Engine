using DCFApixels.DragonECS;
using Karpik.Engine.Client.Network.Core.Systems;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Network.Core;

internal class NetworkClientModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new InitNetworkClientSystem())
            .Add(new UpdateNetworkClientSystem(), CustomLayers.BEGIN_PROGRAM_LAYER)
            .Add(new DestroyNetworkClientSystem());
    }
}