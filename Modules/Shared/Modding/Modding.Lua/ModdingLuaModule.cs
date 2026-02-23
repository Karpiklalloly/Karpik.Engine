using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Lua.Systems;

namespace Karpik.Engine.Shared.Modding.Lua;

public class ModdingLuaModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new UpdateSystem());
    }
}