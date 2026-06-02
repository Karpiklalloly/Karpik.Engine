using System.Numerics;

namespace Karpik.Engine.Shared.Physics.Core;

public interface IPhysicsWorld2D
{
    protected internal void Step(float deltaTime);
    
    public PhysicsBodyHandle CreateBody(int entityId, Vector2 position, float rotation, in BodyConfig bodyConfig, in ShapeConfig shapeConfig);
    
    public void DestroyBody(PhysicsBodyHandle handle);
    
    protected internal void GetTransforms(ReadOnlySpan<PhysicsBodyHandle> handles, Span<Vector2> outPositions, Span<float> outRotations);
    protected internal void GetVelocities(ReadOnlySpan<PhysicsBodyHandle> handles, Span<Vector2> outLinear, Span<float> outAngular);
    protected internal void SetTransforms(ReadOnlySpan<PhysicsBodyHandle> handles, ReadOnlySpan<Vector2> positions, ReadOnlySpan<float> rotations);
    protected internal void SetVelocities(ReadOnlySpan<PhysicsBodyHandle> handles, ReadOnlySpan<Vector2> linear, ReadOnlySpan<float> angular);
    
    public int Raycast(Vector2 start, Vector2 end, PhysicsLayerMask layerMask, Span<RaycastHit2D> results);
        
    public int OverlapCircle(Vector2 center, float radius, PhysicsLayerMask layerMask, Span<RaycastHit2D> results);

    public ReadOnlySpan<CollisionEvent> GetFrameCollisions();

    public void ApplyForce(PhysicsBodyHandle handle, Vector2 force, Vector2 point);
    public void ApplyLinearImpulse(PhysicsBodyHandle handle, Vector2 impulse);
    public float GetMass(PhysicsBodyHandle handle);
    
    public Vector2 GetVelocity(PhysicsBodyHandle handle);
    public void SetVelocity(PhysicsBodyHandle handle, Vector2 linear);
}