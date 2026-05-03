using Karpik.Engine.Modules.Window.Core;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class ImGuiRenderContext : IDisposable
{
    private GraphicsDevice _device = null!;
    private ImGuiRenderer _renderer = null!;
    private CommandList _commandList = null!;
    private int _width;
    private int _height;

    public bool IsInitialized => _renderer is not null;

    public void Init(GraphicsDevice device, IWindow window)
    {
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
        _commandList?.Dispose();
        _renderer?.Dispose();
    }
}
