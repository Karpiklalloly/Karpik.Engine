using System.Reflection;
using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core.Physical;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Shared.AssetManagement.Core;

[Module(-10000)]
public class AssetManagementModuleInstaller : IModuleInstaller, IModuleInstallerListener, IModuleInstallerDestroy
{
    public string Name => "AssetManagement.Core";
    
    private AssetsManager _assetsManager = null!;

    public void OnRegisterServices(ContainerBuilder builder)
    {
        _assetsManager = new AssetsManager(new PhysicalFileSystem());
        builder.Register<IAssetsManager>(_assetsManager);
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        _assetsManager.RegisterLoaders(Assembly.GetExecutingAssembly());
    }

    public void OnAnotherModuleLoaded(IServiceContainer services, IModuleInstaller anotherModuleInstaller, Assembly anotherModuleAssembly)
    {
        if (anotherModuleInstaller.Name == "Graphics.Core")
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