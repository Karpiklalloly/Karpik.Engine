using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Shared.Modding.Lua;

[Module]
public class ModdingLuaModuleInstaller : IModuleInstaller, IModuleInstallerDestroy, IModuleInstallerConfiguratable
{
    public string Name => "Modding.Lua";
 
    private ModManager _modManager;
    
    public void OnRegisterServices(ContainerBuilder builder)
    {
        _modManager = new ModManager();
#if SERVER
        _modManager.Init(ExecutionSide.Server);
#else
        _modManager.Init(ExecutionSide.Client);
#endif
        
        builder.Register<IModManager>(_modManager);
    }

    public void OnConfigure(IServiceResolver services, out IModule? module)
    {
        module = new ModdingLuaModule();
    }

    public void OnConfigureComplete(IServiceResolver services)
    {
        _modManager.LoadMods(services.Get<IAssetsManager>().ModsPath);
    }

    public void Destroy()
    {
        _modManager.Destroy();
    }
}