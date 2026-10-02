using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using NeoVeldrid;

namespace Karpik.Engine.Modules.Window.Headless;

[Export(typeof(IWindow))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class HeadlessWindow : IWindow
{
    private int _width = 800;
    private int _height = 600;

    public event Action? Resized;

    public int Width => _width;
    public int Height => _height;
    public string Title { get; set; } = "KarpikEngine Headless";
    public WindowState WindowState { get; set; } = WindowState.Normal;
    public bool Exists { get; private set; } = true;
    public bool IsResized { get; private set; }

    public void Resize(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (_width == width && _height == height)
        {
            return;
        }

        _width = width;
        _height = height;
        IsResized = true;
        Resized?.Invoke();
    }

    public void ClearResizeFlag()
    {
        IsResized = false;
    }

    public void Close()
    {
        Exists = false;
    }

    public void Dispose()
    {
        Close();
    }
}
