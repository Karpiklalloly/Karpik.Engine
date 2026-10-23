using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Shared.Modding.Lua.Systems;

[RuntimeSystem, SequentialSystem]
internal sealed class LuaUpdateSystem(ModContainer owner, LuaScriptRuntime runtime, int callbackIndex, Time time) : ISystemUpdate
{
    public void Update()
    {
        if (owner.TryStartUpdate()) runtime.InvokeSystem(callbackIndex, time.DeltaTime);
    }
}
