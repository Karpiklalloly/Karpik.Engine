using Karpik.Engine.Core;
using Karpik.Engine.Shared.Modding.Lua.Systems;

namespace Karpik.Engine.Shared.Modding.Lua;

internal class ModdingLuaModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<InitSystem>();
        systems.Add<UpdateSystem>();
    }
}