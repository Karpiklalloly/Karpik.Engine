using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;
using Veldrid.Sdl2;

namespace Karpik.Engine.Modules.Window.Sdl2;

public class SDL2Window : IWindow
{
    [DI] private Application _application = null!;
    private readonly Sdl2Window _window;
    private bool _isResized;

    public event Action? Resized;

    public int Width => _window.Width;
    
    public int Height => _window.Height;

    public string Title
    {
        get => _window.Title;
        set => _window.Title = value;
    }

    public WindowState WindowState
    {
        get => _window.WindowState;
        set => _window.WindowState = value;
    }

    public bool Exists => _window.Exists;

    public bool IsResized
    {
        get
        {
            bool isResized = _isResized;
            _isResized = false;
            return isResized;
        }
    }

    public SDL2Window(Sdl2Window window)
    {
        _window = window;
        _window.Resized += WindowOnResized;
        _window.Closed += WindowOnClosed;
    }

    private void WindowOnClosed()
    {
        _application.Stop();
    }

    public void Dispose()
    {
        _window.Resized -= WindowOnResized;
        _window.Closed -= WindowOnClosed;
    }
    
    private void WindowOnResized()
    {
        _isResized = true;
        Resized?.Invoke();
    }
}
