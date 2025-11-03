using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Karpik.Engine.Shared;

public class BaseSystem
{
    private static ConcurrentDictionary<EcsWorld, EcsCommandBuffer> _worldBuffers = new();
    private static bool _inited = false;
    
    public static void InitWorlds(params Span<EcsWorld> worlds)
    {
        if (_inited) throw new("Worlds have already been inited");
        
        _inited = true;
        foreach (var world in worlds)
        {
            _worldBuffers.TryAdd(world, new EcsCommandBuffer(world));
        }
    }
    
    public static void RunBuffers()
    {
        foreach (var buffer in _worldBuffers.Values)
        {
            buffer.Run();
        }
    }
    
    protected entlong CreateEntity(EcsWorld world)
    {
        lock (world)
        {
            return world.NewEntityLong();
        }
    }
    
    private void DeleteEntity(EcsWorld world, int entity)
    {
        lock (world)
        {
            world.DelEntity(entity);
        }
    }

    protected void Command(EcsWorld world, Action<EcsWorld> action)
    {
        _worldBuffers[world].AddCommand(action);
    }

    protected void SendEvent<T>(EcsEventWorld world, T @event) where T : struct, IEcsComponent
    {
        var entity = CreateEntity(world);
        Pool<T>(world).Add(entity.ID) = @event;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected EcsPool<T> Pool<T>(EcsWorld world) where T : struct, IEcsComponent
    {
        return world.GetPool<T>();
    }
}