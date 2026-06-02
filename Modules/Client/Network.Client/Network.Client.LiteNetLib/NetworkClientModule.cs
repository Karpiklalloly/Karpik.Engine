using Karpik.Engine.Client.Network.LiteNetLib.Systems;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Network.LiteNetLib;

internal class NetworkClientModule : IModule
{
    public void Import(IBuilder b)
    {
        b.Add(new InitNetworkClientSystem());
        b.Add(new DestroyNetworkClientSystem());
        b.Add(new UpdateNetworkClientSystem(), CustomLayers.BEGIN_PROGRAM_LAYER);
    }
}