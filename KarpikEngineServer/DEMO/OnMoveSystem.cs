using System.Numerics;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Server.DEMO;

public class OnMoveSystem : IEcsRunOnEvents<MoveCommand>, IEcsRunOnRequest<MoveCommandRequest>
{
    private EcsDefaultWorld _world = Worlds.Instance.World;

    public void RunOnEvents(Span<MoveCommand> commands)
    {
        // Берём по последней уникальной паре Source и Target, затем суммируем Direction для каждого Target
        var lastPairs = new Dictionary<(int Source, int Target), int>();
        for (int i = 0; i < commands.Length; i++)
        {
            var pair = (commands[i].Source, commands[i].Target);
            lastPairs[pair] = i; // сохраняем индекс последнего вхождения пары
        }

        var directionSums = new Dictionary<int, Vector3>();
        foreach (var kvp in lastPairs)
        {
            var cmd = commands[kvp.Value];
            if (!directionSums.ContainsKey(cmd.Target))
                directionSums[cmd.Target] = Vector3.Zero;
            directionSums[cmd.Target] += cmd.Direction;
        }

        foreach (var pair in directionSums)
        {
            var target = pair.Key;
            var direction = pair.Value;
            var sources = lastPairs
                .Where(x => x.Key.Target == target)
                .Select(x => x.Key.Source);
            
            _world.SendRequest(new MoveCommandRequest()
            {
                Target = _world.FindByNetworkId(target).ID,
                Direction = direction,
                Sources = sources
            });
        }
    }

    public void RunOnRequest(ref MoveCommandRequest evt)
    {
        var entity = _world.GetEntityLong(evt.Target);
        ref var position = ref _world.GetPool<Position>().Get(entity.ID);
        position.X += evt.Direction.X;
        position.Y += evt.Direction.Y;
        position.Z += evt.Direction.Z;
        Console.WriteLine($"Entity {entity.ID} moved to new position: {position.X}, {position.Y}");
    }
}