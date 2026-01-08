using DCFApixels.DragonECS;
using Karpik.Engine.Client.Systems;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client;

internal class InputModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new UpdateSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -1000);
    }
}