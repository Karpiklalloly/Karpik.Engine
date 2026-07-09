using ImGuiNET;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.OpenGL;

public sealed class OpenGLGraphicsBackend : IGraphicsBackend
{
    private readonly GraphicsDevice _device;
    private readonly IMergeThread _mergeThread;
    private readonly Preset2DPipeline _pipeline;
    private readonly ImGuiRenderContext _imgui;
    private readonly IWindow _window;
    private readonly IInputSource _inputSource;
    private readonly Time _time;

    public OpenGLGraphicsBackend(
        GraphicsDevice device,
        IMergeThread mergeThread,
        Preset2DPipeline pipeline,
        ImGuiRenderContext imgui,
        IWindow window,
        IInputSource inputSource,
        Time time)
    {
        _device = device;
        _mergeThread = mergeThread;
        _pipeline = pipeline;
        _imgui = imgui;
        _window = window;
        _inputSource = inputSource;
        _time = time;
    }

    public bool IsHeadless => false;

    public void Initialize()
    {
        _pipeline.Init();
        _imgui.Init(_device, _window);
    }

    public void BeginFrame()
    {
        if (_device.MainSwapchain.Framebuffer.Width != (uint)_window.Width ||
            _device.MainSwapchain.Framebuffer.Height != (uint)_window.Height)
        {
            _device.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
            _imgui.ResizeIfNeeded(_window);
        }

        GraphicsContext.BeginFrame();
    }

    public void BeginMerge()
    {
        _mergeThread.TryBeginMerge();
    }

    public void SubmitScene()
    {
        if (_mergeThread.TryGetCompletedCommandList(out CommandList? commandList, out Fence? submitFence))
        {
            _device.ResetFence(submitFence);
            _device.SubmitCommands(commandList, submitFence);
        }
    }

    public void UpdateImGui(out bool wantsMouse, out bool wantsKeyboard, out bool wantsText)
    {
        _imgui.Update((float)_time.DeltaTime, _inputSource.Snapshot);

        ImGuiIOPtr io = ImGui.GetIO();
        wantsMouse = io.WantCaptureMouse;
        wantsKeyboard = io.WantCaptureKeyboard;
        wantsText = io.WantTextInput || wantsKeyboard;
    }

    public void RenderImGui()
    {
        _imgui.Render();
    }

    public void SwapBuffers()
    {
        _device.SwapBuffers();
    }

    public void Dispose()
    {
    }
}
