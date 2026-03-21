using System.Numerics;
using System.Runtime.InteropServices;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.Shared.Physics.Core;

public readonly struct PhysicsBodyHandle : IEquatable<PhysicsBodyHandle>
{
    public readonly int Value;
    public static readonly PhysicsBodyHandle Invalid = new(-1);
        
    public PhysicsBodyHandle(int value) => Value = value;
    public bool IsValid => Value != -1;
    public bool Equals(PhysicsBodyHandle other) => Value == other.Value;

    public override bool Equals(object? obj)
    {
        return obj is PhysicsBodyHandle handle && Equals(handle);
    }

    public override int GetHashCode()
    {
        return Value;
    }
}


public enum BodyType : byte { Static, Kinematic, Dynamic }
public enum ShapeType : byte { Box, Circle }

public struct BodyConfig 
{
    public BodyType Type;
    public float Mass;
    public float Friction;
    public float Restitution;
    public bool IsSensor;
    // Маски коллизий (битовые флаги)
    public uint CategoryBits;
    public uint MaskBits;
}

[StructLayout(LayoutKind.Explicit, Size = 12)]
public struct ShapeConfig 
{
    [FieldOffset(0)] 
    public ShapeType Type;

    // Эти поля делят одни и те же 8 байт памяти!
    [FieldOffset(4)] 
    public float CircleRadius;

    [FieldOffset(4)] 
    public Vector2 BoxSize;

    // Фабричные методы для удобства и защиты от ошибок
    public static ShapeConfig Circle(float radius) => new ShapeConfig { 
        Type = ShapeType.Circle, 
        CircleRadius = radius 
    };

    public static ShapeConfig Box(Vector2 size) => new ShapeConfig { 
        Type = ShapeType.Box, 
        BoxSize = size 
    };
}

public struct RaycastHit2D 
{
    public int Entity;              // Какую ECS-сущность задели
    public PhysicsBodyHandle Body;  // Какое физическое тело задели
    public Vector2 Point;           // Точка попадания
    public Vector2 Normal;          // Нормаль поверхности
    public float Fraction;          // Дистанция (0.0 до 1.0 от начала луча)
}

public struct CollisionEvent 
{
    public int EntityA;
    public int EntityB;
    public Vector2 Normal;
    public float Impulse;
}

public readonly struct PhysicsLayerMask : IEquatable<PhysicsLayerMask>
{
    public readonly uint Value;

    public PhysicsLayerMask(uint value) => Value = value;

    // Позволяет писать mask1 | mask2
    public static PhysicsLayerMask operator |(PhysicsLayerMask a, PhysicsLayerMask b) => new PhysicsLayerMask(a.Value | b.Value);
    public static PhysicsLayerMask operator &(PhysicsLayerMask a, PhysicsLayerMask b) => new PhysicsLayerMask(a.Value & b.Value);
    public static PhysicsLayerMask operator ~(PhysicsLayerMask a) => new PhysicsLayerMask(~a.Value);

    public static readonly PhysicsLayerMask All = new PhysicsLayerMask(uint.MaxValue);
    public static readonly PhysicsLayerMask None = new PhysicsLayerMask(0);
        
    // Неявное преобразование из/в uint для удобства
    public static implicit operator uint(PhysicsLayerMask mask) => mask.Value;
    public static implicit operator PhysicsLayerMask(uint value) => new PhysicsLayerMask(value);

    public bool Equals(PhysicsLayerMask other) => Value == other.Value;
}

[NetworkedComponent]
public struct Transform2D : IEcsComponent 
{
    [NetworkedField]
    public Vector2 Position;
    [NetworkedField]
    public float Rotation;
        
    public Vector2 Forward => new Vector2(MathF.Cos(Rotation), MathF.Sin(Rotation));
}

public struct Velocity2D : IEcsComponent 
{
    public Vector2 Linear;
    public float Angular;
}

public struct PhysicsBodyRef : IEcsComponent 
{
    public PhysicsBodyHandle Handle;
}

public struct CreateBodyRequest : IEcsComponent 
{
    public BodyConfig BodyConfig;
    public ShapeConfig ShapeConfig;
}

public struct DestroyBodyRequest : IEcsComponent;

public struct TeleportRequest : IEcsComponent 
{
    public Vector2 Position;
    public float Rotation;
}

public struct SetVelocityRequest : IEcsComponent 
{
    public Vector2 Linear;
    public float Angular;
}