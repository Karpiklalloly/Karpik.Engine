using System.Text;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace Karpik.Engine.Client.Graphics.OpenGL;

[Module(-100)]
public class GraphicsOpenGLInstaller : IInstaller, IInstallerConfiguratable, IInstallerDestroy
{
    public string Name => "Graphics.OpenGL";
    
    private GraphicsDevice _graphicsDevice = null!;
    private IMergeThread _mergeThread = null!;
    private bool _colorSrgb = true;
    
    private List<AssetHandle<ShaderAsset>> _shaderAssets = [];
    
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
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
        _mergeThread = new MergeThread();
        container.Register(_graphicsDevice);
        container.Register(_mergeThread);
        container.Register(new Preset2DPipeline());
        module = null;
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
        var textFrag = assetsManager.LoadAssetAsync<ShaderAsset>("Shaders/TextSdf.frag");
        var vert = assetsManager.LoadAssetAsync<ShaderAsset>("Shaders/2D.vert");
        
        JobHandle<JobHandle> handle = Job.Run<JobHandle>(async () =>
        {
            try
            {
                var fragData = await frag;
                var textFragData = await textFrag;
                var vertData = await vert;
                _shaderAssets.Add(fragData);
                _shaderAssets.Add(textFragData);
                _shaderAssets.Add(vertData);
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
        _mergeThread.Dispose();
        _graphicsDevice.Dispose();
        foreach (var handle in _shaderAssets)
        {
            if (handle.IsValid)
            {
                handle.Dispose();
            }
        }
        _shaderAssets.Clear();
    }
}
