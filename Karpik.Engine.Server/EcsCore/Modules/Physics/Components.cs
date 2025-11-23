using System.Numerics;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Server;

[Serializable]
public struct RigidBody : IEcsComponent
{
    public double Mass;
    [JsonIgnore]
    public double InverseMass;
    public double MomentOfInertia;
    [JsonIgnore]
    public double InverseMomentOfInertia;
    public double Restitution;
    public double StaticFriction;
    public double DynamicFriction;
    public BodyType Type;
    public CollisionMode Mode;

    [JsonConstructor]
    public RigidBody(double mass, double momentOfInertia, double restitution, double staticFriction, double dynamicFriction, BodyType type, CollisionMode mode)
    {
        Mass = mass;
        InverseMass = mass > 0 ? 1 / mass : 0;
        MomentOfInertia = momentOfInertia;
        InverseMomentOfInertia = momentOfInertia > 0 ? 1 / momentOfInertia : 0;
        Restitution = restitution;
        StaticFriction = staticFriction;
        DynamicFriction = dynamicFriction;
        Type = type;
        Mode = mode;
    }
    
    public enum BodyType
    {
        Static,
        Dynamic,
        Kinematic
    }
    
    public enum CollisionMode
    {
        Discrete,
        ContinuousSpeculative
    }
}

[Serializable]
public struct ColliderBox : IEcsComponent
{
    public Vector2 Size;
    public Vector2 Offset;
    public double RotationOffset;
    public bool IsTrigger;
}

[Serializable]
public struct ColliderCircle : IEcsComponent
{
    public double Radius;
    public Vector2 Offset;
    public double RotationOffset;
    public bool IsTrigger;
}

[AllowedInWorlds(typeof(EcsEventWorld), nameof(EcsEventWorld))]
public struct CollisionsEvent : IEcsComponentEvent
{
    public int Source { get; set; }
    public int Target { get; set; }
    public List<CollisionInfo> Infos;

    public CollisionsEvent()
    {
        Infos = new List<CollisionInfo>();
    }
}

public struct CollisionInfo
{
    public entlong Other;
    public Vector2 Normal;
}

public struct Box2DData
{
    public int EcsEntityId;
}