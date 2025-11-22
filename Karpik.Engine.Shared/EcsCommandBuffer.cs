using System.Collections.Concurrent;

namespace Karpik.Engine.Shared;

public class EcsCommandBuffer
{
    private readonly EcsWorld _world;
    private readonly ConcurrentQueue<Action<EcsWorld>> _commands = new();

    public EcsCommandBuffer(EcsWorld world) => _world = world;
    
    public void AddCommand(Action<EcsWorld> command)
    {
        _commands.Enqueue(command);
    }
    
    public void Run()
    {
        while (_commands.TryDequeue(out var command))
        {
            command(_world);
        }
    }
}


// TODO: Кодген под каждый мир
public class EcsDefaultCommandBuffer : EcsCommandBuffer
{
    public EcsDefaultCommandBuffer(EcsDefaultWorld world) : base(world) { }
}