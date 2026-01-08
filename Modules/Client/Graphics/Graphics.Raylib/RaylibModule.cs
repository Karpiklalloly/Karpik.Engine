using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.GRaylib.Systems;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.GRaylib;

public class RaylibModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new InitSystem())
            .Add(new BeginContextSystem(), CustomLayers.BEGIN_PROGRAM_LAYER)
            .Add(new PreEndContextSystem(), EcsConsts.POST_END_LAYER, -100)
            .Add(new EndContextSystem(), CustomLayers.END_PROGRAM_LAYER)
            .Add(new DestroySystem());
    }
}