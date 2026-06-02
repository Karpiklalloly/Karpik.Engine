using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

internal class InputModuleEcs : IModule
{
    public void Import(IBuilder b)
    {
        b.Add(new UpdateSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -1000);
        b.Add(new DestroySystem(), CustomLayers.END_PROGRAM_LAYER, 1000);
    }
}