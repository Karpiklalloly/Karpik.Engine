using Karpik.Engine.Shared.DEMO;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Server.DEMO;

public class DemoModule : IEcsModule
{
    private List<int> _destroyedIds = new();

    public DemoModule(List<int> destroyedIds)
    {
        _destroyedIds = destroyedIds;
    }

    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new DamageSystem())
            .Add(new OnMoveSystem())
            .Add(new OnJumpSystem())
            .Add(new OnModReloadSystem())
            .AddCaller<MoveCommand>()
            .AddCaller<MoveCommandRequest>()
            .AddCaller<JumpCommand>()
            .AddCaller<ReloadModsCommand>();
    }
}