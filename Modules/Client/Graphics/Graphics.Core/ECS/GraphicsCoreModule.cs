using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

public class GraphicsCoreModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<GraphicsCoreBeginSystem>(CustomLayers.BEGIN_PROGRAM_LAYER, -1800);
        systems.Add<ImGuiBeginSystem>(CustomLayers.BEGIN_PROGRAM_LAYER, -1750);
        systems.Add<GraphicsCoreMergeSystem>(CustomLayers.BEGIN_PROGRAM_LAYER, -1700);
        systems.Add<GraphicsLoadTestRenderPrepareSystem>();
        systems.Add<GraphicsCoreSubmitSceneSystem>(CustomLayers.END_PROGRAM_LAYER, 1700);
        systems.Add<ImGuiDebugPanelSystem>(CustomLayers.END_PROGRAM_LAYER, 1740);
        systems.Add<ImGuiRenderSystem>(CustomLayers.END_PROGRAM_LAYER, 1750);
        systems.Add<GraphicsCoreSwapBuffersSystem>(CustomLayers.END_PROGRAM_LAYER, 1800);
    }
}
