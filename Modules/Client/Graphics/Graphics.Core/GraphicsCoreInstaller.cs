using DCFApixels.DragonECS;
using System;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

[Module(-101)]
public class GraphicsCoreInstaller : IModule, IModuleConfiguratable, IModuleDestroy
{
    public string Name => "Graphics.Core";
    private readonly ImGuiRenderContext _imguiRenderContext = new();
    
    public void OnRegisterServices(IServiceRegister services)
    {
        var overlayState = new ImGuiOverlayState();
        if (Environment.GetEnvironmentVariable("KARPIK_IMGUI_ENABLED") == "1")
        {
            overlayState.SetEnabled(true);
        }

        services.Register(new GraphicsCameraState());
        services.Register(overlayState);
        services.Register(_imguiRenderContext);
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
        container.Register<IEcsModule>(new GraphicsCoreModule());
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
    
    public void Destroy()
    {
        _imguiRenderContext.Dispose();
    }
}
