using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;

namespace Karpik.Engine.Client.Graphics.Core;

public class GraphicsCoreBeginSystem(
    IGraphicsBackend backend,
    ImGuiOverlayState overlayState,
    InputCaptureState captureState)
    : ISystemMainThreadFrameBegin
{
    public void MainThreadFrameBegin()
    {
        backend.BeginFrame();

        if (!overlayState.Enabled)
        {
            overlayState.ClearCapture();
            captureState.Clear();
        }
    }
}

public class GraphicsCoreMergeSystem(
    IGraphicsBackend backend,
    GraphicsCameraState cameraState) : ISystemMainThreadFrameBegin
{
    public void MainThreadFrameBegin()
    {
        backend.BeginMerge(cameraState.ActiveCamera);
    }
}

public class GraphicsCoreSubmitSceneSystem(IGraphicsBackend backend) : ISystemRender
{
    public void Render()
    {
        backend.SubmitScene();
    }
}

public class ImGuiRenderSystem(ImGuiOverlayState overlay, IGraphicsBackend backend) : ISystemRender
{
    public void Render()
    {
        if (!overlay.Enabled)
        {
            return;
        }

        backend.RenderImGui();
    }
}

public class GraphicsCoreSwapBuffersSystem(IGraphicsBackend backend) : ISystemRender
{
    public void Render()
    {
        backend.SwapBuffers();
    }
}
