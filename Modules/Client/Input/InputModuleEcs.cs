using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

internal class InputModuleEcs : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<PublishInputSystem>(CustomLayers.BEGIN_PROGRAM_LAYER, -1000);
        systems.Add<ConsumeInputSystem>(CustomLayers.BEGIN_PROGRAM_LAYER, -1000);
        systems.Add<DestroySystem>(CustomLayers.END_PROGRAM_LAYER, 1000);
    }
}
