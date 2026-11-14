using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Shared.Modding.Lua.Systems;

[RuntimeSystem, SequentialSystem]
internal sealed class LuaUpdateSystem : ISystemUpdate
{
    private ModContainer? _owner;
    private LuaScriptRuntime? _runtime;
    private readonly int _callbackIndex;
    private readonly Time _time;

    internal LuaUpdateSystem(ModContainer owner, LuaScriptRuntime runtime, int callbackIndex, Time time)
    {
        _owner = owner;
        _runtime = runtime;
        _callbackIndex = callbackIndex;
        _time = time;
    }

    public void Update()
    {
        ModContainer owner = _owner ?? throw new ObjectDisposedException(nameof(LuaUpdateSystem));
        if (owner.TryStartUpdate()) _runtime!.InvokeSystem(_callbackIndex, _time.DeltaTime);
    }

    // Initial pipeline/registry shells may outlive a revision; they must not retain its VM or capabilities.
    internal void Retire()
    {
        _owner = null;
        _runtime = null;
    }
}
