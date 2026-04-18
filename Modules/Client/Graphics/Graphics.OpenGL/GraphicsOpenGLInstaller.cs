using System.Text;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.OpenGL.AssetManagement;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
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
        _graphicsDevice = VeldridStartup.CreateDefaultOpenGLGraphicsDevice(gdOptions, services.Get<Sdl2Window>()!,
            GraphicsBackend.OpenGL);
        container.Register(_graphicsDevice);
        container.Register<ITextureFactory>(new VeldridTextureFactory(_graphicsDevice));
        container.Register<IShaderFactory>(new VeldridShaderFactory(_graphicsDevice));
        container.Register<IPipelineFactory>(new VeldridPipelineFactory(_graphicsDevice));
        container.Register<IGraphicsContext>(new VeldridGraphicsContext(_graphicsDevice));
        container.Register<IMergeThread>(new MergeThread(_graphicsDevice));
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        IAssetsManager? assetsManager = services.Get<IAssetsManager>();
        if (assetsManager is null)
        {
            throw new NullReferenceException("AssetsManager is null!");
        }

        GraphicsDevice? device = services.Get<GraphicsDevice>();
        if (device is null)
        {
            throw new NullReferenceException("GraphicsDevice is null!");
        }
        
        var frag = assetsManager.LoadAssetAsync<ShaderAsset>("Shaders/2D.frag");
        var vert = assetsManager.LoadAssetAsync<ShaderAsset>("Shaders/2D.vert");
        
        JobHandle<JobHandle> handle = Job.Run<JobHandle>(async () =>
        {
            try
            {
                var fragData = await frag;
                var vertData = await vert;
                var factory = device.ResourceFactory;
                string vertexShader = Encoding.UTF8.GetString(vertData.Asset.ShaderBytes);
                string fragShader = Encoding.UTF8.GetString(fragData.Asset.ShaderBytes);
                Shader[]? shaders = factory.CreateFromSpirv(
                    new ShaderDescription(ShaderStages.Vertex, vertData.Asset.ShaderBytes, "main"),
                    new ShaderDescription(ShaderStages.Fragment, fragData.Asset.ShaderBytes, "main")
                );
                int a = 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        });
    }
    
    public void Destroy()
    {
        _graphicsDevice.Dispose();
    }
}