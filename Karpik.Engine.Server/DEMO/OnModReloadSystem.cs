using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.Modding;

namespace Karpik.Engine.Server.DEMO;

public class OnModReloadSystem : IEcsRunOnEvent<ReloadModsCommand>
{
    [DI] private AssetsManager _assetsManager;
    [DI] private ModManager _modManager;
    
    public void RunOnEvent(ref ReloadModsCommand evt)
    {
        _modManager.ReloadAllMods(_assetsManager.ModsPath);
    }
}