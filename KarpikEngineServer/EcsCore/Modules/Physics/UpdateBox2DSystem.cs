using System.Numerics;
using Box2D.NET.Bindings;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Server;

public class UpdateBox2DSystem : IEcsRunParallel
{
    public const int SUB_STEPS = 4;
    [DI] private B2.WorldId _physicsWorld;
    [DI] private EcsEventWorld _eventWorld;
    [DI] private EcsDefaultWorld _world;
    
    public void RunParallel()
    {
        B2.WorldStep(_physicsWorld, (float)Time.FixedDeltaTime, SUB_STEPS);
        var contacts = B2.WorldGetContactEvents(_physicsWorld);
        unsafe
        {
            // On contact
            for (int i = 0; i < contacts.beginCount; i++)
            {
                var e = contacts.beginEvents[i];
                var dataA = (Box2DData*)B2.ShapeGetUserData(e.shapeIdA);
                var dataB = (Box2DData*)B2.ShapeGetUserData(e.shapeIdB);
                var c = new CollisionsEvent
                {
                    Source = dataA->EcsEntityId,
                    Target = dataB->EcsEntityId
                };
                c.Infos ??= [];
                c.Infos.Add(new CollisionInfo()
                {
                    Other = _world.GetEntityLong(dataB->EcsEntityId),
                    Normal = new Vector2(e.manifold.normal.x, e.manifold.normal.y)
                });
                _eventWorld.SendEvent(c);
            }
            
            // On discontact
            for (int i = 0; i < contacts.endCount; i++)
            {
                var e = contacts.endEvents[i];
                if (B2.ShapeIsValid(e.shapeIdA) && B2.ShapeIsValid(e.shapeIdB))
                {
                    
                }
            }
        }
    }
}