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
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = new RaylibModule();
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