using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.GRaylib;

[Module]
public class GraphicsRaylibInstaller : IModule
{
    public string Name => "Graphics.Raylib";
    
    public void OnRegisterServices(IServiceRegister services)
    {
        var renderer = new RaylibRenderer();
        services
            .Register<IRenderer>(renderer)
            .Register<IWindow>(new RaylibWindow())
            .Register<ICamera>(renderer.MainCamera3D);
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = new RaylibModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}