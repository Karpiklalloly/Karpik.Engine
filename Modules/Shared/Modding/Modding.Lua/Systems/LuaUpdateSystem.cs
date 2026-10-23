using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Shared.Modding.Lua.Systems;

// Runtime instances are boxed once at registration; class systems belong to generated DI factories.
[SequentialSystem]
internal readonly struct LuaUpdateSystem(ModContainer owner, LuaScriptRuntime runtime, int callbackIndex, Time time) : ISystemUpdate
{
    public void Update()
    {
        if (owner.TryStartUpdate()) runtime.InvokeSystem(callbackIndex, time.DeltaTime);
    }
}
