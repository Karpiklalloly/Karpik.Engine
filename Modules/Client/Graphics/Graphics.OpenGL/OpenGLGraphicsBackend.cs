using System.Diagnostics;
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
    private readonly ClientFrameMetrics _clientFrameMetrics;
    private bool _sceneSubmittedForPresent;
    private int _lastGpuTimestampSequence;

    public OpenGLGraphicsBackend(
        GraphicsDevice device,
        IMergeThread mergeThread,
        Preset2DPipeline pipeline,
        ImGuiRenderContext imgui,
        IWindow window,
        IInputSource inputSource,
        Time time,
        ClientFrameMetrics clientFrameMetrics)
    {
        _device = device;
        _mergeThread = mergeThread;
        _pipeline = pipeline;
        _imgui = imgui;
        _window = window;
        _inputSource = inputSource;
        _time = time;
        _clientFrameMetrics = clientFrameMetrics;
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
        _sceneSubmittedForPresent = false;
        if (_mergeThread.TryTakeCompletedCommandList(out CommandList? commandList, out Fence? submitFence))
        {
            _clientFrameMetrics.PublishMergeAvailability(isReady: true);
            _device.ResetFence(submitFence);
            _device.SubmitCommands(commandList, submitFence);
            _sceneSubmittedForPresent = true;
        }
        else
        {
            _clientFrameMetrics.PublishMergeAvailability(isReady: false);
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
        if (!_sceneSubmittedForPresent)
        {
            return;
        }

        _imgui.Render();
    }

    public void SwapBuffers()
    {
        if (!_sceneSubmittedForPresent)
        {
            return;
        }

        long startedAt = Stopwatch.GetTimestamp();
        _device.SwapBuffers();
        _clientFrameMetrics.PublishPresent(Stopwatch.GetTimestamp() - startedAt);
        PublishGpuTimestampIfAvailable();
        _sceneSubmittedForPresent = false;
    }

    public void Dispose()
    {
    }

    private void PublishGpuTimestampIfAvailable()
    {
        if (_device is not IGpuTimestampProvider provider
            || !provider.TryGetLatestGpuTimestamp(out GpuTimestampSample sample)
            || sample.Sequence == _lastGpuTimestampSequence)
        {
            return;
        }

        _lastGpuTimestampSequence = sample.Sequence;
        _clientFrameMetrics.PublishGpuCommandNanoseconds(sample.Nanoseconds);
    }
}
