using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

internal class WindowCoreModule : IModule
{
    public void Import(IBuilder b)
    {
        b.Add(new UpdateSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -2000);
    }
}