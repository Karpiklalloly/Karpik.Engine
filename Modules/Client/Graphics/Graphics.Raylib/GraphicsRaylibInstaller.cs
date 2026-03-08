using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.GRaylib;

[Module]
public class GraphicsRaylibInstaller : IModule, IModuleConfiguratable, IModuleHotReload
{
    public string Name => "Graphics.Raylib";

    public void OnRegisterServices(IServiceRegister services)
    {
        var window = new RaylibWindow();
        services.Register<IWindow>(window);
        services.Register(window);

        var renderer = new RaylibRenderer();
        services.Register<IRenderer>(renderer);
        services.Register(renderer);

        var camera = new RaylibCamera();
        services.Register<ICamera>(camera);
        services.Register(camera);
        
        // Use the same 2D camera that renderer creates internally
        services.Register<ICamera2D>(renderer.MainCamera2D);
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
        container.Register<IEcsModule>(new RaylibModule());
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
    }

    public byte[] OnPrepareHotReload(IServiceContainer services)
    {
        return [];
    }

    public bool OnHotReload(byte[] data, IServiceContainer services)
    {
        return true;
    }
}