using System.Numerics;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

/// <summary>
/// Server-side collision event system - handles collectibles, death zones, finish zones
/// </summary>
public class ServerCollisionEventSystem : ISystemUpdate
{
    class PlayerAspect : EcsAspect
    {
        public EcsReadonlyPool<Player> Player = Inc;
    }
    
    class CollectibleAspect : EcsAspect
    {
        public EcsReadonlyPool<Collectible> Collectible = Inc;
    }
    
    [DI] private IPhysicsWorld2D _physicsWorld = null!;
    [DI] private DefaultWorld _world = null!;

    private List<int> _entitiesToDestroy = [];

    public void Update()
    {
        var collisions = _physicsWorld.GetFrameCollisions();
        
        for (int i = 0; i < collisions.Length; i++)
        {
            var collision = collisions[i];
            ProcessCollision(collision.EntityA, collision.EntityB, collision.Normal, _entitiesToDestroy);
        }
        
        foreach (var entity in _entitiesToDestroy)
        {
            if (_world.Get(entity).IsAlive)
            {
                _world.Del(entity);
            }
        }
        
        _entitiesToDestroy.Clear();
    }
    
    private void ProcessCollision(int entityA, int entityB, Vector2 normal, List<int> entitiesToDestroy)
    {
        bool aIsPlayer = _world.Base.GetPool<Player>().Has(entityA);
        bool bIsPlayer = _world.Base.GetPool<Player>().Has(entityB);
        
        if (!aIsPlayer && !bIsPlayer)
            return;
            
        int playerEntity = aIsPlayer ? entityA : entityB;
        int otherEntity = aIsPlayer ? entityB : entityA;

        if (_world.Base.GetPool<Collectible>().Has(otherEntity))
        {
            ref var collectible = ref _world.Base.GetPool<Collectible>().Get(otherEntity);
            if (!collectible.IsCollected)
            {
                collectible.IsCollected = true;
                if (!entitiesToDestroy.Contains(otherEntity))
                {
                    entitiesToDestroy.Add(otherEntity);
                }
            }
        }
    }
}
