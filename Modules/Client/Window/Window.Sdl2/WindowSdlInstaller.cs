using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Karpik.Engine.Modules.Window.Sdl2;

[Module(-200)]
public class WindowSdlInstaller : IModule, IModuleDestroy
{
    private Sdl2Window _window = null!;
    private SDL2Window _sdl2Window = null!;

    public string Name => "Window.Sdl2";
    
    public void OnRegisterServices(IServiceRegister services)
    {
        WindowCreateInfo windowCI = new WindowCreateInfo
        {
            X = 100,
            Y = 100,
            WindowWidth = 800,
            WindowHeight = 600,
            WindowInitialState = WindowState.Normal,
            WindowTitle = "KarpikEngine"
        };

        _window = VeldridStartup.CreateWindow(windowCI);
        _sdl2Window = new SDL2Window(_window);
        services.Register(_window);
        services.Register<IInputSource>(new SDL2InputSource(_window));
        services.Register<IWindow>(_sdl2Window);
    }

    public void Destroy()
    {
        _sdl2Window.Dispose();
        _window.Close();
    }
}