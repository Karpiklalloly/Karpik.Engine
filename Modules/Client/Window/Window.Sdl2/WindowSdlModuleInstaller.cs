using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Karpik.Engine.Modules.Window.Sdl2;

[Module(ModuleScope.Engine, -200)]
public class WindowSdlModuleInstaller : IModuleInstaller
{
    public string Name => "Window.Sdl2";
    
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(x =>
        {
            return VeldridStartup.CreateWindow(new WindowCreateInfo
            {
                X = 100,
                Y = 100,
                WindowWidth = 800,
                WindowHeight = 600,
                WindowInitialState = WindowState.Normal,
                WindowTitle = "KarpikEngine"
            });
        })
        .AsSelf()
        .SingleInstance()
        .OnRelease(static window =>
        {
            if (window.Exists)
            {
                window.Close();
            }
        });
    }
}