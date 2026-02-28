using System.Numerics;
using DCFApixels.DragonECS;

namespace Karpik.Engine.Shared.Physics.ECS;

public struct RigidBody : IEcsComponent
{
    public float Mass;
    public float InverseMass;
    public Vector2 Velocity;
    public float AngularVelocity;
    public Vector2 Force;
    public float Torque;
    public float LinearDamping;
    public float AngularDamping;
    public RigidBodyType BodyType;
    public bool IsSimulated;
    public bool AllowSleep;
    public bool IsAwake;
}

public enum RigidBodyType : byte
{
    Static = 0,
    Dynamic = 1,
    Kinematic = 2
}

public struct BoxColliderComponent : IEcsComponent
{
    public Vector2 Offset;
    public Vector2 Size;
    public float Angle;
    
    public float Density;
    public float Friction;
    public float Restitution;
    public bool IsSensor;
    public CollisionCategory Category;
    public CollisionCategory Mask;
}

public struct CircleColliderComponent : IEcsComponent
{
    public Vector2 Offset;
    public float Radius;
    
    public float Density;
    public float Friction;
    public float Restitution;
    public bool IsSensor;
    public CollisionCategory Category;
    public CollisionCategory Mask;
}

public struct PolygonColliderComponent : IEcsComponent
{
    public Vector2 Offset;
    public float Angle;
    public Vector2 Centroid;
    public ReadOnlySpan<Vector2> Vertices;
    public ReadOnlySpan<Vector2> Normals;
    
    public float Density;
    public float Friction;
    public float Restitution;
    public bool IsSensor;
    public CollisionCategory Category;
    public CollisionCategory Mask;
}

public struct ChainColliderComponent : IEcsComponent
{
    public Vector2 Offset;
    public ReadOnlySpan<Vector2> Vertices;
    public float Friction;
    public float Restitution;
    public bool IsSensor;
    public CollisionCategory Category;
    public CollisionCategory Mask;
}