using DCFApixels.DragonECS;

namespace Karpik.Game.Modules;

public class UIModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new DrawPlayerHealthSystem(), EcsConsts.END_LAYER)
            .Add(new OpenMenuOnPauseSystem(), EcsConsts.END_LAYER);
    }
}