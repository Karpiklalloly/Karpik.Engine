using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Shared.Modding.Lua.Systems;

public sealed class BeginSystem(IModsLifecycleManager lifecycle) : ISystemBegin
{
    public void Begin()
    {
        IReadOnlyList<ModContainer> containers = lifecycle.Containers;

        for (int i = 0; i < containers.Count; i++)
        {
            containers[i].Start();
        }
    }
}

[SequentialSystem]
public sealed class UpdateSystem(
    IModsLifecycleManager lifecycle,
    Time time) : ISystemUpdate
{
    public void Update()
    {
        IReadOnlyList<ModContainer> containers = lifecycle.Containers;
        double dt = time.DeltaTime;
        
        // TODO: mods run as one group; use individual adapters for interleaving.
        for (int i = 0; i < containers.Count; i++)
        {
            containers[i].Update(dt);
        }
    }
}

[SequentialSystem]
public sealed class FixedUpdateSystem(
    IModsLifecycleManager lifecycle,
    Time time) : ISystemFixedUpdate
{
    public void FixedUpdate()
    {
        IReadOnlyList<ModContainer> containers = lifecycle.Containers;
        double dt = time.FixedDeltaTime;
        
        for (int i = 0; i < containers.Count; i++)
        {
            containers[i].FixedUpdate(dt);
        }
    }
}
