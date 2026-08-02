using Autofac;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

[Module(ModuleScope.Engine, -101)]
public class GraphicsCoreEngineModuleInstaller : IModuleInstaller, IModuleInstallerConfiguratable
{
    public string Name => "Graphics.Core";
    
    public void OnRegisterServices(ContainerBuilder builder)
    {

        builder.RegisterType<GraphicsLoadTestSettings>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GraphicsLoadTestResources>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ImGuiRenderContext>()
            .AsSelf()
            .SingleInstance();

        builder.Register(x =>
        {
            var overlayState = new ImGuiOverlayState();
            if (Environment.GetEnvironmentVariable("KARPIK_IMGUI_ENABLED") == "1")
            {
                overlayState.SetEnabled(true);
            }

            return overlayState;
        })
        .AsSelf()
        .SingleInstance();
    }

    public void OnConfigure(IServiceResolver services)
    {
        services.Resolve<IGraphicsBackend>().Initialize();
    }

    public void OnConfigureComplete(IServiceResolver services)
    {
        
    }
}