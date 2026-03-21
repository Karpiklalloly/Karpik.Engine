using System.Numerics;
using DragonExtensions;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;


internal class InputSystem : IEcsRunOnEvent<PlatformerInputCommand>
{
    private const float MOVE_SPEED = 8.0f;
    private const float JUMP_FORCE = 12.0f;
    private const float MAX_VELOCITY_X = 10.0f;
    private const float JUMP_COOLDOWN = 0.2f;
 
    class NetworkIdAspect : EcsAspect
    {
        public EcsReadonlyPool<NetworkId> netId = Inc;
    }

    class Aspect : EcsAspect
    {
        public EcsPool<PhysicsBodyRef> body = Inc;
        public EcsPool<Transform2D> transform = Inc;
        public EcsPool<Velocity2D> velocity = Opt;
        public EcsPool<JumpState> input = Opt;
    }

    [DI] private EcsDefaultWorld _world = null!;
    [DI] private IPhysicsWorld2D _physicsWorld2D = null!;
    [DI] private Time _time = null!;
    
    public void RunOnEvent(ref PlatformerInputCommand evt)
    {
        var entity = FindByNetworkId(evt.Target, _world);

        var span = _world.Where(out Aspect a);
        if (!span.Has(entity.ID)) return;

        var id = entity.ID;
        ref var transform = ref a.transform.Get(id);
        ref var component = ref a.body.Get(id);
        ref var state = ref a.input.TryAddOrGet(id);
        
        float currentTime = (float)_time.TotalTime;
        
        bool canJump = state.CanJump && 
                       (currentTime - state.LastJumpTime > JUMP_COOLDOWN);

        Console.WriteLine(evt.MoveX);
        Console.WriteLine(evt.Jump);
        var force = new Vector2(
            evt.MoveX * MOVE_SPEED,
            evt.Jump ? JUMP_FORCE : 0);
        
        Console.WriteLine(force);
        // a.velocity.TryAddOrGet(id).Linear += force;
        Console.WriteLine($"Position = {transform.Position}");
        _physicsWorld2D.ApplyForce(
            component.Handle,
            force,
            transform.Position);
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
