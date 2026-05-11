using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class GraphicsCoreInitSystem : ISystemInit
{
    [DI] private Preset2DPipeline _pipelines = null!;
    [DI] private GraphicsDevice _device = null!;
    [DI] private IWindow _window = null!;
    [DI] private ImGuiRenderContext _imgui = null!;

    public void Init()
    {
        _pipelines.Init();
        _imgui.Init(_device, _window);
    }
}

// TODO: не инжектится после рефаторинга
public class GraphicsCoreBeginSystem : ISystemBegin
{
    [DI] private GraphicsDevice _device = null!;
    [DI] private IWindow _window = null!; // Инъекция окна для отслеживания размера
    [DI] private ImGuiRenderContext _imgui = null!;
    [DI] private ImGuiOverlayState _imguiOverlay = null!;
    [DI] private InputCaptureState _inputCapture = null!;
    
    public void Begin()
    {
        if (_device.MainSwapchain.Framebuffer.Width != (uint)_window.Width ||
            _device.MainSwapchain.Framebuffer.Height != (uint)_window.Height)
        {
            _device.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
            _imgui.ResizeIfNeeded(_window);
        }

        if (!_imguiOverlay.Enabled)
        {
            _imguiOverlay.ClearCapture();
            _inputCapture.Clear();
        }

        GraphicsContext.BeginFrame();
    }
}

public class GraphicsCoreMergeSystem : ISystemBegin
{
    [DI] private IMergeThread _mergeThread = null!;

    public void Begin()
    {
        _mergeThread.BeginMerge();
    }
}

public class GraphicsCoreSubmitSceneSystem : ISystemRender
{
    [DI] private IMergeThread _mergeThread = null!;
    [DI] private GraphicsDevice _device = null!;

    public void Render()
    {
        // Ждем, если мердж еще не успел закончиться (обычно он уже готов)
        _mergeThread.WaitForCompletion();

        // Отправляем готовые команды в GPU
        _device.SubmitCommands(_mergeThread.GetCommandList());
    }
}

public class ImGuiRenderSystem : ISystemRender
{
    [DI] private ImGuiOverlayState _overlay = null!;
    [DI] private ImGuiRenderContext _imgui = null!;

    public void Render()
    {
        if (!_overlay.Enabled)
        {
            return;
        }

        _imgui.Render();
    }
}

public class GraphicsCoreSwapBuffersSystem : ISystemRender
{
    [DI] private GraphicsDevice _device = null!;

    public void Render()
    {
        _device.SwapBuffers();
    }
}
