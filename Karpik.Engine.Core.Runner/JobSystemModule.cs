using DCFApixels.DragonECS;

namespace Karpik.Engine.Core;

public class JobSystemModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new BeginContextSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -1000)
            .Add(new EndContextSystem(), CustomLayers.END_PROGRAM_LAYER, 1000)
            .Add(new DestroySystem());
    }
}