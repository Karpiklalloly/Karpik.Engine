using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.Shared.ECS;

public class BaseSystem
{
    class NetworkIdAspect : EcsAspect
    {
        public EcsReadonlyPool<NetworkId> netId = Inc;
    }
    
    private static ConcurrentDictionary<EcsWorld, EcsCommandBuffer> _worldBuffers = new();
    
    internal static void InitWorlds(params Span<EcsWorld> worlds)
    {
        foreach (var world in worlds)
        {
            _worldBuffers.TryAdd(world, new EcsCommandBuffer(world));
        }
    }
    
    internal static void RunBuffers()
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

    protected entlong FindByNetworkId(int networkId, EcsWorld world)
    {
        var span = world.Where(out NetworkIdAspect aspect);
        foreach (var e in span) 
        {
            var id = aspect.netId.Get(e);
            if (id.Id == networkId)
            {
                return world.GetEntityLong(e);
            }
        }
        return entlong.NULL;
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