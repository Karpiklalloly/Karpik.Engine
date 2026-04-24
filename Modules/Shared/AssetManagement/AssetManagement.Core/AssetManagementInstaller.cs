using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core.Physical;

namespace Karpik.Engine.Shared.AssetManagement.Core;

[Module(-10000)]
public class AssetManagementInstaller : IModule, IModuleListener, IModuleConfiguratable, IModuleDestroy
{
    public string Name => "AssetManagement.Core";
    
    private AssetsManager _assetsManager = null!;

    public void OnRegisterServices(IServiceRegister services)
    {
        _assetsManager = new AssetsManager(new PhysicalFileSystem());
        services.Register<IAssetsManager>(_assetsManager);
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        _assetsManager.RegisterLoaders(Assembly.GetExecutingAssembly());
    }

    public void OnAnotherModuleLoaded(IServiceContainer services, IModule anotherModule, Assembly anotherModuleAssembly)
    {
        if (anotherModule.Name == "Graphics.Core")
        {
            
        }
        _assetsManager.RegisterLoaders(anotherModuleAssembly);
        _assetsManager.RegisterSavers(anotherModuleAssembly);
    }

    public void Destroy()
    {
        _assetsManager.ReleaseAll();
    }
}