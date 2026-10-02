using Autofac;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

[Module(ModuleScope.Engine, -101)]
public class GraphicsCoreEngineModuleInstaller : IModuleInstaller
{
    public string Name => "Graphics.Core";
    
    public void OnRegisterServices(ContainerBuilder builder)
    {
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
}