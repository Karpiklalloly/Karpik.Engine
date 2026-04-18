using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

internal class WindowCoreModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new UpdateSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -2000);
    }
}