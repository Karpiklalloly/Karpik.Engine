using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.Shared.Modding.Lua.Systems;

internal class UpdateSystem : IEcsRunLate, IEcsInit
{
    [DI] private ModManager _modManager = null!;
    
    public void Init()
    {
        _modManager.StartMods();
    }
    
    public void RunLate()
    {
        _modManager.UpdateMods();
    }
}