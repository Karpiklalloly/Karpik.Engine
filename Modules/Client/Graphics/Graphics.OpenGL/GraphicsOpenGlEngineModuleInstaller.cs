using Autofac;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Karpik.Engine.Client.Graphics.OpenGL;

[Module(ModuleScope.Engine, -100)]
public class GraphicsOpenGlEngineModuleInstaller : IModuleInstaller
{
    public string Name => "Graphics.OpenGL";
    
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.RegisterType<Preset2DPipeline>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MergeThread>()
            .AsSelf()
            .As<IMergeThread>()
            .SingleInstance();

        builder.RegisterType<OpenGLGraphicsBackend>()
            .As<IGraphicsBackend>()
            .SingleInstance();
        
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

                return VeldridStartup.CreateDefaultOpenGLGraphicsDevice(
                    options,
                    context.Resolve<Sdl2Window>(),
                    GraphicsBackend.OpenGL);
            })
            .As<GraphicsDevice>()
            .SingleInstance();
    }
}
