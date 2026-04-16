using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Jobs;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Karpik.Engine.Client.Graphics.OpenGL;

[Module(-100)]
public class GraphicsOpenGLInstaller : IModule, IModuleConfiguratable, IModuleDestroy
{
    public string Name => "Graphics.OpenGL";
    
    private GraphicsDevice _graphicsDevice = null!;
    private bool _colorSrgb = true;
    
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
        var gdOptions = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: null,
            syncToVerticalBlank: false,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true,
            preferStandardClipSpaceYDirection: true,
            _colorSrgb
        );

        JobSystem? jobSystem = services.Get<JobSystem>();
        if (jobSystem is null)
        {
            throw new Exception("JobSystem is not added to IServiceContainer");
        }

        _graphicsDevice = VeldridStartup.CreateGraphicsDevice(services.Get<Sdl2Window>(), gdOptions);
        container.Register(_graphicsDevice);
        container.Register<ITextureFactory>(new VeldridTextureFactory(_graphicsDevice));
        container.Register<IShaderFactory>(new VeldridShaderFactory(_graphicsDevice));
        container.Register<IPipelineFactory>(new VeldridPipelineFactory(_graphicsDevice));
        container.Register<IGraphicsContext>(new VeldridGraphicsContext(_graphicsDevice));
        container.Register<IMergeThread>(new MergeThread(_graphicsDevice, jobSystem));
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
    
    public void Destroy()
    {
        _graphicsDevice.Dispose();
    }
}