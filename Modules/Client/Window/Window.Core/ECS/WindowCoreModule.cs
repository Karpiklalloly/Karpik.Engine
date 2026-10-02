using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

internal class WindowCoreModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<UpdateSystem>(CustomLayers.BEGIN_PROGRAM_LAYER, -2000);
    }
}