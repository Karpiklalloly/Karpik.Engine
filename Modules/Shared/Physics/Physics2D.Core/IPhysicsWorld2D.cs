using OpenTK.Mathematics;

namespace Karpik.Engine.Shared.Physics.Core;

public interface IPhysicsWorld2D
{
    protected internal void Step(double deltaTime);
    
    public PhysicsBodyHandle CreateBody(int entityId, Vector2d position, float rotation, in BodyConfig bodyConfig, in ShapeConfig shapeConfig);
    
    public void DestroyBody(PhysicsBodyHandle handle);
    
    protected internal void GetTransforms(ReadOnlySpan<PhysicsBodyHandle> handles, Span<Vector2d> outPositions, Span<float> outRotations);
    protected internal void GetVelocities(ReadOnlySpan<PhysicsBodyHandle> handles, Span<Vector2d> outLinear, Span<double> outAngular);
    protected internal void SetTransforms(ReadOnlySpan<PhysicsBodyHandle> handles, ReadOnlySpan<Vector2d> positions, ReadOnlySpan<float> rotations);
    protected internal void SetVelocities(ReadOnlySpan<PhysicsBodyHandle> handles, ReadOnlySpan<Vector2d> linear, ReadOnlySpan<double> angular);
    
    public int Raycast(Vector2d start, Vector2d end, PhysicsLayerMask layerMask, Span<RaycastHit2D> results);
        
    public int OverlapCircle(Vector2d center, double radius, PhysicsLayerMask layerMask, Span<RaycastHit2D> results);

    public ReadOnlySpan<CollisionEvent> GetFrameCollisions();

    public void ApplyForce(PhysicsBodyHandle handle, Vector2d force, Vector2d point);
    public void ApplyLinearImpulse(PhysicsBodyHandle handle, Vector2d impulse);
    public float GetMass(PhysicsBodyHandle handle);
    
    public Vector2d GetVelocity(PhysicsBodyHandle handle);
    public void SetVelocity(PhysicsBodyHandle handle, Vector2d linear);
}