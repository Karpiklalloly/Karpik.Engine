using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Karpik.Engine.Shared.DragonECS;
using Network;

namespace Karpik.Engine.Client;

public class SetLocalPlayerSystem : BaseSystem, IEcsRunOnEvent<SetLocalPlayerTargetRpc>
{
    class NetworkIdAspect : EcsAspect
    {
        public EcsReadonlyPool<NetworkId> netId = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    
    public void RunOnEvent(ref SetLocalPlayerTargetRpc evt)
    {
        Logger.Instance.Log(nameof(SetLocalPlayerSystem), $"Got SetLocalPlayerTargetRpc for netId {evt.LocalPlayerNetId}");
        var entity = _world.FindByNetworkId(evt.LocalPlayerNetId);
        _world.GetPool<LocalPlayer>().Add(entity.ID);
    }
}