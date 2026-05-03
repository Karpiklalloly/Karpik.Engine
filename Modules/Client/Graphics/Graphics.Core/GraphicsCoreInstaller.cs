using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

[Module(-101)]
public class GraphicsCoreInstaller : IModule, IModuleConfiguratable, IModuleDestroy
{
    public string Name => "Graphics.Core";
    private readonly ImGuiRenderContext _imguiRenderContext = new();
    
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(new GraphicsCameraState());
        services.Register(new ImGuiOverlayState());
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
