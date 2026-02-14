using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core.Physical;

namespace Karpik.Engine.Shared.AssetManagement.Core;

[Module(-100)]
public class AssetManagementInstaller : IModule, IModuleListener, IModuleConfiguratable
{
    public string Name => "AssetManagement.Core";
    
    private AssetsManager _assetsManager = null!;

    public void OnRegisterServices(IServiceRegister services)
    {
        _assetsManager = new AssetsManager(new PhysicalFileSystem());
        services.Register<IAssetsManager>(_assetsManager);
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        _assetsManager.RegisterLoaders(Assembly.GetExecutingAssembly());
    }

    public void OnAnotherModuleLoaded(IServiceContainer services, IModule anotherModule, Assembly anotherModuleAssembly)
    {
        _assetsManager.RegisterLoaders(anotherModuleAssembly);
        _assetsManager.RegisterSavers(anotherModuleAssembly);
    }
}