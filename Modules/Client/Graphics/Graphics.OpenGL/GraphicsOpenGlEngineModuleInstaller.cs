using Autofac;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using NeoVeldrid;
using NeoVeldrid.Sdl2;
using NeoVeldrid.StartupUtilities;

namespace Karpik.Engine.Client.Graphics.OpenGL;

[Module(ModuleScope.Engine, -100)]
public class GraphicsOpenGlEngineModuleInstaller : IModuleInstaller
{
    public string Name => "Graphics.OpenGL";
    
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(context =>
            {
                var options = new GraphicsDeviceOptions(
                    debug: false,
                    swapchainDepthFormat: null,
                    syncToVerticalBlank: false,
                    resourceBindingModel: ResourceBindingModel.Improved,
                    preferDepthRangeZeroToOne: true,
                    preferStandardClipSpaceYDirection: true,
                    swapchainSrgbFormat: true);

                return NeoVeldridStartup.CreateDefaultOpenGLGraphicsDevice(
                    options,
                    context.Resolve<Sdl2Window>(),
                    GraphicsBackend.OpenGL);
            })
            .As<GraphicsDevice>()
            .SingleInstance();
    }
}
