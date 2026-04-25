using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

public class GraphicsCoreModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new GraphicsCoreInitSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -1800);
        b.Add(new GraphicsCoreBeginSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -1800);
        b.Add(new GraphicsCoreMergeSystem(), CustomLayers.END_PROGRAM_LAYER, 1700);
        b.Add(new GraphicsCoreSubmitSystem(), CustomLayers.END_PROGRAM_LAYER, 1800);
    }
}