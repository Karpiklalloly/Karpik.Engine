using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Microsoft.Extensions.DependencyInjection;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Karpik.Engine.Modules.Window.Sdl2;

[Module(-200)]
public class WindowSdlModuleInstaller : IModuleInstaller, IModuleInstallerDestroy
{
    private Sdl2Window _window = null!;
    private SDL2Window _sdl2Window = null!;

    public string Name => "Window.Sdl2";
    
    public void OnRegisterServices(ContainerBuilder builder)
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
        builder.Register(_window);
        builder.Register<IInputSource>(new SDL2InputSource(_window));
        builder.Register<IWindow>(_sdl2Window);
    }

    public void Destroy()
    {
        _sdl2Window.Dispose();
        _window.Close();
    }
}