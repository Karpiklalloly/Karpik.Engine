using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

internal class InputModuleEcs : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new UpdateSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -1000);
    }
}