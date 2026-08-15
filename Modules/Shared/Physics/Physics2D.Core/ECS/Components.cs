using System.Runtime.InteropServices;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Network.Core;
using OpenTK.Mathematics;
using Vector2 = System.Numerics.Vector2;

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

    public static bool operator ==(PhysicsBodyHandle left, PhysicsBodyHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PhysicsBodyHandle left, PhysicsBodyHandle right)
    {
        return !(left == right);
    }
}


public enum BodyType : byte { Static, Kinematic, Dynamic }
public enum ShapeType : byte { Box, Circle }

public struct BodyConfig 
{
    public BodyType Type;
    public double Mass;
    public double Friction;
    public double Restitution;
    public bool IsSensor;
    public bool IgnoreGravity;
    public uint CategoryBits;
    public uint MaskBits;
}

[StructLayout(LayoutKind.Explicit, Size = 20)]
public struct ShapeConfig 
{
    [FieldOffset(0)] 
    public ShapeType Type;

    [FieldOffset(4)] 
    public double CircleRadius;

    [FieldOffset(4)] 
    public Vector2d BoxSize;

    // Фабричные методы для удобства и защиты от ошибок
    public static ShapeConfig Circle(double radius) => new ShapeConfig { 
        Type = ShapeType.Circle, 
        CircleRadius = radius 
    };

    public static ShapeConfig Box(Vector2d size) => new ShapeConfig { 
        Type = ShapeType.Box, 
        BoxSize = size 
    };
}

public struct RaycastHit2D 
{
    public int Entity;              // Какую ECS-сущность задели
    public PhysicsBodyHandle Body;  // Какое физическое тело задели
    public Vector2d Point;           // Точка попадания
    public Vector2d Normal;          // Нормаль поверхности
    public float Fraction;          // Дистанция (0.0 до 1.0 от начала луча)
}

public struct CollisionEvent 
{
    public int EntityA;
    public int EntityB;
    public Vector2d Normal;
    public double Impulse;
}

public readonly struct PhysicsLayerMask : IEquatable<PhysicsLayerMask>
{
    public readonly uint Value;

    public PhysicsLayerMask(uint value) => Value = value;

    // Позволяет писать mask1 | mask2
    public static PhysicsLayerMask operator |(PhysicsLayerMask a, PhysicsLayerMask b) => new PhysicsLayerMask(a.Value | b.Value);
    public static PhysicsLayerMask operator &(PhysicsLayerMask a, PhysicsLayerMask b) => new PhysicsLayerMask(a.Value & b.Value);
    public static PhysicsLayerMask operator ~(PhysicsLayerMask a) => new PhysicsLayerMask(~a.Value);
        
    // Неявное преобразование из/в uint для удобства
    public static implicit operator uint(PhysicsLayerMask mask) => mask.Value;
    public static implicit operator PhysicsLayerMask(uint value) => new PhysicsLayerMask(value);

    public bool Equals(PhysicsLayerMask other) => Value == other.Value;
}

public struct Velocity2D : IEcsComponent 
{
    public Vector2d Linear;
    public double Angular;
}

public struct PhysicsBodyRef : IEcsComponent 
{
    public PhysicsBodyHandle Handle;
}

public struct PhysicsBodyDefinition : IEcsComponent
{
    public BodyConfig BodyConfig;
    public ShapeConfig ShapeConfig;
}

public struct CreateBodyRequest : IEcsComponent 
{
    public BodyConfig BodyConfig;
    public ShapeConfig ShapeConfig;
}

public struct DestroyBodyRequest : IEcsComponent;

public struct TeleportRequest : IEcsComponent 
{
    public Vector2d Position;
    public float Rotation;
}

public struct SetVelocityRequest : IEcsComponent 
{
    public Vector2d Linear;
    public double Angular;
}
