using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;

namespace Karpik.Engine.Client.Graphics.Core;

public class GraphicsCoreInitSystem : ISystemInit
{
    [DI] private IGraphicsBackend _backend = null!;

    public void Init()
    {
        _backend.Initialize();
    }
}

// TODO: не инжектится после рефаторинга
public class GraphicsCoreBeginSystem : ISystemBegin
{
    [DI] private IGraphicsBackend _backend = null!;
    [DI] private ImGuiOverlayState _imguiOverlay = null!;
    [DI] private InputCaptureState _inputCapture = null!;

    public void Begin()
    {
        _backend.BeginFrame();

        if (!_imguiOverlay.Enabled)
        {
            _imguiOverlay.ClearCapture();
            _inputCapture.Clear();
        }
    }
}

public class GraphicsCoreMergeSystem : ISystemBegin
{
    [DI] private IGraphicsBackend _backend = null!;

    public void Begin()
    {
        _backend.BeginMerge();
    }
}

public class GraphicsCoreSubmitSceneSystem : ISystemRender
{
    [DI] private IGraphicsBackend _backend = null!;

    public void Render()
    {
        _backend.SubmitScene();
    }
}

public class ImGuiRenderSystem : ISystemRender
{
    [DI] private ImGuiOverlayState _overlay = null!;
    [DI] private IGraphicsBackend _backend = null!;

    public void Render()
    {
        if (!_overlay.Enabled)
        {
            return;
        }

        _backend.RenderImGui();
    }
}

public class GraphicsCoreSwapBuffersSystem : ISystemRender
{
    [DI] private IGraphicsBackend _backend = null!;

    public void Render()
    {
        _backend.SwapBuffers();
    }
}
