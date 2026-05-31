using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Log;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class SetLocalPlayerSystem : IEcsRunOnEvent<SetLocalPlayerTargetRpc>
{
    class NetworkIdAspect : EcsAspect
    {
        public EcsReadonlyPool<NetworkId> netId = Inc;
    }
    
    [DI] private DefaultWorld _world;
    [DI] private ClientReconnectTokenStore _reconnectTokenStore = null!;
    
    public void RunOnEvent(ref SetLocalPlayerTargetRpc evt)
    {
        Logger.Instance.Log(nameof(SetLocalPlayerSystem), $"Got SetLocalPlayerTargetRpc for netId {evt.LocalPlayerNetId}");
        _reconnectTokenStore.Save(evt.ReconnectToken);
        StoreClientReconnectToken(evt.ReconnectToken);

        var entity = FindOrCreateByNetworkId(evt.LocalPlayerNetId, _world.Base);
        var localPlayerPool = _world.Base.GetPool<LocalPlayer>();
        if (!localPlayerPool.Has(entity.ID))
        {
            localPlayerPool.Add(entity.ID);
        }

        var sessionPool = _world.Base.GetPool<PlayerSession>();
        if (sessionPool.Has(entity.ID))
        {
            sessionPool.Get(entity.ID).ReconnectToken = evt.ReconnectToken;
        }
        else
        {
            sessionPool.Add(entity.ID).ReconnectToken = evt.ReconnectToken;
        }
    }

    private void StoreClientReconnectToken(long reconnectToken)
    {
        var pool = _world.Base.GetPool<ClientReconnectSession>();
        foreach (var entity in _world.Base.Where(EcsStaticMask.Inc<ClientReconnectSession>().Build()))
        {
            pool.Get(entity).ReconnectToken = reconnectToken;
            return;
        }

        pool.Add(_world.New().ID).ReconnectToken = reconnectToken;
    }

    protected entlong FindOrCreateByNetworkId(int networkId, EcsWorld world)
    {
        var entity = FindByNetworkId(networkId, world);
        if (entity.IsAlive)
        {
            return entity;
        }

        var created = world.NewEntity();
        world.GetPool<NetworkId>().Add(created).Id = networkId;
        return world.GetEntityLong(created);
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
}
