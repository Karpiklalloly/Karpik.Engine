using System.Numerics;

namespace Karpik.Engine.Shared.Physics.Core;

public interface IPhysicsWorld2D
{
    public void Step(float deltaTime);
    
    public PhysicsBodyHandle CreateBody(int entityId, Vector2 position, float rotation, in BodyConfig bodyConfig, in ShapeConfig shapeConfig);
    
    public void DestroyBody(PhysicsBodyHandle handle);
    
    public void GetTransforms(ReadOnlySpan<PhysicsBodyHandle> handles, Span<Vector2> outPositions, Span<float> outRotations);
    public void GetVelocities(ReadOnlySpan<PhysicsBodyHandle> handles, Span<Vector2> outLinear, Span<float> outAngular);
    public void SetTransforms(ReadOnlySpan<PhysicsBodyHandle> handles, ReadOnlySpan<Vector2> positions, ReadOnlySpan<float> rotations);
    
    public int Raycast(Vector2 start, Vector2 end, PhysicsLayerMask layerMask, Span<RaycastHit2D> results);
        
    public int OverlapCircle(Vector2 center, float radius, PhysicsLayerMask layerMask, Span<RaycastHit2D> results);

    public ReadOnlySpan<CollisionEvent> GetFrameCollisions();

    public void ApplyForce(PhysicsBodyHandle handle, Vector2 force, Vector2 point);
    public void ApplyLinearImpulse(PhysicsBodyHandle handle, Vector2 impulse);
    public float GetMass(PhysicsBodyHandle handle);
}