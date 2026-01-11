using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Base.Physical;

namespace Karpik.Engine.Shared.AssetManagement.Base;

[Module(-100)]
public class AssetManagementInstaller : IModule, IModuleListener
{
    public string Name => "AssetManagement.Core";
    
    private AssetsManager _assetsManager = null!;

    public void OnRegisterServices(IServiceRegister services)
    {
        _assetsManager = new AssetsManager(new PhysicalFileSystem());
        services.Register<IAssetsManager>(_assetsManager);
        
        _assetsManager.RegisterLoaders(Assembly.GetExecutingAssembly());
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }

    public void OnAnotherModuleLoaded(IServiceContainer services, IModule anotherModule, Assembly anotherModuleAssembly)
    {
        _assetsManager.RegisterLoaders(anotherModuleAssembly);
        _assetsManager.RegisterSavers(anotherModuleAssembly);
    }
}