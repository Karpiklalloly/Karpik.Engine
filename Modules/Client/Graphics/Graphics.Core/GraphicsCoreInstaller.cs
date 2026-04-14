using Veldrid;
using Veldrid.StartupUtilities;
using Karpik.Engine.Core;
using Veldrid.Sdl2;

namespace Karpik.Engine.Client.Graphics.Core;

[Module]
public class GraphicsCoreInstaller : IModule, IModuleConfiguratable, IModuleDestroy
{
    public string Name => "Graphics.Core";
    
    private GraphicsDevice _graphicsDevice = null!;
    private bool _colorSrgb = true;
    
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
        // TODO: Перенести в реализации (opengl, vulcan и прочее)
        var gdOptions = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: null,
            syncToVerticalBlank: false,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true,
            preferStandardClipSpaceYDirection: true,
            _colorSrgb
        );

        _graphicsDevice = VeldridStartup.CreateGraphicsDevice(services.Get<Sdl2Window>(), gdOptions);
        container.Register(_graphicsDevice);
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        throw new NotImplementedException();
    }
    
    public void Destroy()
    {
        _graphicsDevice.Dispose();
    }
}
