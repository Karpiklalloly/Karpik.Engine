using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Systems;

namespace Karpik.Engine.Shared.Modding;

public class ModdingLuaModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new UpdateSystem());
    }
}