using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Log;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

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
        var entity = FindByNetworkId(evt.LocalPlayerNetId, _world);
        _world.GetPool<LocalPlayer>().Add(entity.ID);
    }
}