using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Base;

namespace Karpik.Engine.Shared.Modding;

[Module]
public class ModdingInstaller : IModule
{
    public string Name => "Modding.Lua";
 
    private ModManager _modManager;
    
    public void OnRegisterServices(IServiceRegister services)
    {
        _modManager = new ModManager();
#if SERVER
        _modManager.Init(ExecutionSide.Server);
#else
        _modManager.Init(ExecutionSide.Client);
#endif
        
        services.Register<IModManager>(_modManager);
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = new ModdingLuaModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        _modManager.LoadMods(services.Get<IAssetsManager>().ModsPath);
    }
}