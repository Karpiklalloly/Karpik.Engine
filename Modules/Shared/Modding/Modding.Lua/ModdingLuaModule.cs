using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Modding.Lua.Systems;

namespace Karpik.Engine.Shared.Modding.Lua;

internal class ModdingLuaModule : IModule
{
    public void Import(IBuilder b)
    {
        b.Add(new InitSystem());
        b.Add(new UpdateSystem());
    }
}