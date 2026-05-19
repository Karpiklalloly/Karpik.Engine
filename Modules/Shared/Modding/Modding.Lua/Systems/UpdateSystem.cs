using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.Shared.Modding.Lua.Systems;

internal class InitSystem : ISystemInit
{
    [DI] private ModManager _modManager = null!;
    
    public void Init()
    {
        _modManager.StartMods();
    }
}

internal class UpdateSystem : ISystemLateUpdate
{
    [DI] private ModManager _modManager = null!;
    
    public void LateUpdate()
    {
        _modManager.UpdateMods();
    }
}