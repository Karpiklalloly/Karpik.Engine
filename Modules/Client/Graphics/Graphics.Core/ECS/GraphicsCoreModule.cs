using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

public class GraphicsCoreModule : IModule
{
    public void Import(IBuilder b)
    {
        b.Add(new GraphicsCoreInitSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -1800);
        b.Add(new GraphicsCoreBeginSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -1800);
        b.Add(new ImGuiBeginSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -1750);
        b.Add(new GraphicsCoreMergeSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -1700);
        b.Add(new GraphicsCoreSubmitSceneSystem(), CustomLayers.END_PROGRAM_LAYER, 1700);
        b.Add(new ImGuiDebugPanelSystem(), CustomLayers.END_PROGRAM_LAYER, 1740);
        b.Add(new ImGuiRenderSystem(), CustomLayers.END_PROGRAM_LAYER, 1750);
        b.Add(new GraphicsCoreSwapBuffersSystem(), CustomLayers.END_PROGRAM_LAYER, 1800);
    }
}
