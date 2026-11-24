using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using NeoVeldrid;
using ImGuiNET;

namespace Karpik.Engine.Client.Graphics.Core;

[Export(typeof(ImGuiRenderContext))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class ImGuiRenderContext : IDisposable
{
    private GraphicsDevice _device = null!;
    private ImGuiRenderer _renderer = null!;
    private CommandList _commandList = null!;
    private ImGuiSettingsFile? _settings;
    private nint _imGuiContext;
    private bool _disposed;
    private int _width;
    private int _height;

    public bool IsInitialized => _renderer is not null;

    public void Init(GraphicsDevice device, IWindow window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsInitialized)
        {
            return;
        }

        _device = device;
        _width = Math.Max(1, window.Width);
        _height = Math.Max(1, window.Height);
        _renderer = new ImGuiRenderer(
            device,
            device.MainSwapchain.Framebuffer.OutputDescription,
            _width,
            _height);
        _imGuiContext = ImGui.GetCurrentContext();
        _settings = new ImGuiSettingsFile(ImGui.GetIO(), Environment.CurrentDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        _commandList = device.ResourceFactory.CreateCommandList();
        _commandList.Name = "ImGui Command List";
    }

    public void ResizeIfNeeded(IWindow window)
    {
        if (!IsInitialized)
        {
            return;
        }

        int width = Math.Max(1, window.Width);
        int height = Math.Max(1, window.Height);
        if (width == _width && height == _height)
        {
            return;
        }

        _width = width;
        _height = height;
        _renderer.WindowResized(width, height);
    }

    public void Update(float deltaSeconds, InputSnapshot snapshot)
    {
        _renderer.Update(deltaSeconds, snapshot);
    }

    public void Render()
    {
        _commandList.Begin();
        _commandList.SetFramebuffer(_device.MainSwapchain.Framebuffer);
        _renderer.Render(_device, _commandList);
        _commandList.End();
        _device.SubmitCommands(_commandList);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _commandList?.Dispose();
            _renderer?.Dispose();
        }
        finally
        {
            try
            {
                // NeoVeldrid disposes device resources; this owner releases its native ImGui context.
                if (_imGuiContext != 0) ImGui.DestroyContext(_imGuiContext);
                _imGuiContext = 0;
            }
            finally { _settings?.Dispose(); }
        }
    }
}
