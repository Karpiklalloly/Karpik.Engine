using DCFApixels.DragonECS;
using Karpik.Engine.Client.UIToolkit.Systems;

namespace Karpik.Engine.Client.UIToolkit;

internal class UIToolkitModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new UpdateSystem(), EcsConsts.POST_END_LAYER, 100);
    }
}