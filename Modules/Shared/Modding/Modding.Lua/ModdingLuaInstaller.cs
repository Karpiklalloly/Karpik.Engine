using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Shared.Modding.Lua;

[Module]
public class ModdingLuaInstaller : IInstaller, IInstallerDestroy, IInstallerConfiguratable
{
    public string Name => "Modding.Lua";
 
    private ModManager _modManager;
    
    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer)
    {
        _modManager = new ModManager();
#if SERVER
        _modManager.Init(ExecutionSide.Server);
#else
        _modManager.Init(ExecutionSide.Client);
#endif
        
        services.Register<IModManager>(_modManager);
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        module = new ModdingLuaModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        _modManager.LoadMods(services.Get<IAssetsManager>().ModsPath);
    }

    public void Destroy()
    {
        _modManager.Destroy();
    }
}