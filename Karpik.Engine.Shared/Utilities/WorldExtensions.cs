using Network;

namespace Karpik.Engine.Shared;

public static class WorldExtensions
{
    class NetworkIdAspect : EcsAspect
    {
        public EcsReadonlyPool<NetworkId> netId = Inc;
    }
    
    public static entlong FindByNetworkId(this EcsWorld world, int networkId)
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
}