# Physics 2D API Specification

## Обзор

API для работы с 2D физикой. Полная абстракция от физического движка (Aether.Physics2D). Интеграция с Dragon ECS.

## Компоненты (ECS)

Все компоненты реализуют `IEcsComponent`.

### RigidBodyComponent

Данные физического тела. Реализует `IEcsComponent`.

```csharp
using DCFApixels.DragonECS.Core;

public struct RigidBodyComponent : IEcsComponent
{
    public float Mass;
    public float InverseMass;
    public Vector2Float Velocity;
    public float AngularVelocity;
    public Vector2Float Force;
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

### Collider Components

Отдельные компоненты для каждого типа коллайдера (ECS-подход: компонент = тип). Все реализуют `IEcsComponent`.

```csharp
using DCFApixels.DragonECS.Core;

public struct BoxColliderComponent : IEcsComponent
{
    public Vector2Float Offset;
    public Vector2Float Size;
    public float Angle;
    
    public float Density;
    public float Friction;
    public float Restitution;
    public bool IsSensor;
    public CollisionCategory Category;
    public CollisionCategory Mask;
}
    public CollisionCategory Category;
    public CollisionCategory Mask;
}

public struct CircleColliderComponent : IEcsComponent
{
    public Vector2Float Offset;
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
    public Vector2Float Offset;
    public float Angle;
    public Vector2Float Centroid;
    public ReadOnlySpan<Vector2Float> Vertices;
    public ReadOnlySpan<Vector2Float> Normals;
    
    public float Density;
    public float Friction;
    public float Restitution;
    public bool IsSensor;
    public CollisionCategory Category;
    public CollisionCategory Mask;
}

public struct ChainColliderComponent : IEcsComponent
{
    public Vector2Float Offset;
    public ReadOnlySpan<Vector2Float> Vertices;
    public float Friction;
    public float Restitution;
    public bool IsSensor;
    public CollisionCategory Category;
    public CollisionCategory Mask;
}
```

### ColliderComponent (Legacy - deprecated)

> Устаревший универсальный компонент. Рекомендуется использовать специализированные компоненты выше.

```csharp
// DEPRECATED - используйте BoxColliderComponent, CircleColliderComponent и т.д.
public struct ColliderComponent
{
    public ColliderShapeType ShapeType;    // тип формы
    public Vector2Float Offset;            // смещение относительно body
    public float Angle;                    // поворот
    
    // Box
    public Vector2Float Size;
    
    // Circle  
    public float Radius;
    
    // Polygon
    public ReadOnlySpan<Vector2Float> Vertices;
    
    public float Density;                 // плотность
    public float Friction;                 // трение
    public float Restitution;              // упругость
    public bool IsSensor;                  // тригер/коллайдер
    public CollisionCategory Category;    // категория
    public CollisionCategory Mask;        // маска коллизий
}

public enum CollisionCategory : uint
{
    Default = 0x0001,
    Player = 0x0002,
    Enemy = 0x0004,
    Wall = 0x0008,
    Projectile = 0x0010,
    Trigger = 0x0020
}
```

### PhysicsStateComponent

Состояние для синхронизации.

```csharp
public struct PhysicsStateComponent : IEcsComponent
{
    public Vector2Float Position;
    public float Rotation;         // в градусах
    public bool IsDirty;
}
```

## Интерфейсы

### IPhysicsWorld

Главный интерфейс физического мира.

```csharp
public interface IPhysicsWorld : IDisposable
{
    // Конфигурация
    Vector2Float Gravity { get; set; }
    int VelocityIterations { get; set; }
    int PositionIterations { get; set; }
    
    // Управление телами
    Entity CreateBody(Entity entity, RigidBodyComponent rigidBody, ColliderComponent collider);
    void DestroyBody(Entity entity);
    
    // Получить данные
    bool TryGetBody(Entity entity, out IPhysicsBody body);
    bool HasBody(Entity entity);
    
    // Синхронизация
    void Step(float deltaTime);
    void SyncToEcs();
    
    // Queries
    bool Raycast(RaycastInput input, out RaycastHit hit);
    int RaycastAll(RaycastInput input, Span<RaycastHit> hits);
    
    bool Overlap(OverlapInput input, Span<Entity> entities);
    int OverlapAll(OverlapInput input, Span<Entity> entities);
}
```

### IPhysicsBody

Интерфейс тела в физическом мире.

```csharp
public interface IPhysicsBody
{
    Entity Entity { get; }
    
    Vector2Float Position { get; set; }
    float Rotation { get; set; }  // градусы
    
    Vector2Float LinearVelocity { get; set; }
    float AngularVelocity { get; set; }
    
    float Mass { get; }
    float InverseMass { get; }
    float Inertia { get; }
    float InverseInertia { get; }
    
    RigidBodyType BodyType { get; }
    bool IsAwake { get; set; }
    
    // Управление
    void ApplyForce(Vector2Float force);
    void ApplyForceAtPoint(Vector2Float force, Vector2Float point);
    void ApplyTorque(float torque);
    void ApplyLinearImpulse(Vector2Float impulse);
    void ApplyAngularImpulse(float impulse);
    
    // Коллайдеры
    int GetColliderCount();
    IPhysicsCollider GetCollider(int index);
    IPhysicsCollider AddCollider(ColliderComponent collider);
    void RemoveCollider(int index);
}
```

### IPhysicsCollider

Интерфейс коллайдера.

```csharp
public interface IPhysicsCollider
{
    IPhysicsBody Body { get; }
    ColliderShapeType ShapeType { get; }
    
    Vector2Float Offset { get; set; }
    float Angle { get; set; }
    
    float Friction { get; set; }
    float Restitution { get; set; }
    float Density { get; set; }
    bool IsSensor { get; set; }
    
    CollisionCategory Category { get; set; }
    CollisionCategory Mask { get; set; }
}
```

## Queries

### RaycastInput / RaycastHit

```csharp
public readonly struct RaycastInput
{
    public Vector2Float Origin;
    public Vector2Float Direction;
    public float MaxDistance;
    public CollisionCategory Mask;
}

public readonly struct RaycastHit
{
    public Entity Entity;
    public Vector2Float Point;
    public Vector2Float Normal;
    public float Fraction;
    public IPhysicsCollider Collider;
}
```

### OverlapInput

```csharp
public readonly struct OverlapInput
{
    public Vector2Float Position;
    public ColliderShapeType ShapeType;
    public Vector2Float Size;      // для box
    public float Radius;           // для circle
    public CollisionCategory Mask;
}
```

## События

События реализуются через компоненты в ECS (Event-based подход Dragon).

### CollisionEvent

```csharp
public struct CollisionEvent : IEcsComponent
{
    public int EntityA;
    public int EntityB;
    public Vector2Float ContactPoint;
    public Vector2Float ContactNormal;
    public float Penetration;
}
```

### TriggerEvent

```csharp
public struct TriggerEnterEvent : IEcsTagComponent { }
public struct TriggerStayEvent : IEcsTagComponent { }
public struct TriggerExitEvent : IEcsTagComponent { }

public struct TriggerEventData : IEcsComponent
{
    public int TriggerEntity;
    public int OtherEntity;
}
```

## Системы

### PhysicsStepSystem

Шаг физики. Выполняет симуляцию.

```csharp
[PhysicsPhase(PhysicsPhase.Step)]
public partial struct PhysicsStepSystem : ISystem
{
    public void Run(ref EcsPipeline.Builder b)
    {
        b.Injection(out IPhysicsWorld world);
        
        b.Update<PhysicsStepProcess>(Layer.Physics);
    }
}

[UpdateInGroup(typeof(PhysicsStepProcess))]
public partial struct PhysicsStepProcess
{
    public void Run(ref EcsPipeline.Builder b, ref EcsWorld world, float delta)
    {
        // 1. Apply forces
        // 2. Step simulation
        // 3. Handle collisions
    }
}
```

### PhysicsSyncSystem

Синхронизация между ECS и физическим миром.

```csharp
[PhysicsPhase(PhysicsPhase.SyncToEcs)]
public partial struct PhysicsSyncSystem : ISystem
{
    public void Run(ref EcsPipeline.Builder b)
    {
        b.Injection(out IPhysicsWorld world);
        
        // Sync position/rotation from physics to ECS
    }
}
```

## Модуль Physics

Интеграция с Dragon ECS через `IEcsModule`.

```csharp
public class PhysicsModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Inject(_physicsWorld);
        
        b.Add(new PhysicsInitSystem());
        b.Add(new PhysicsStepSystem());
        b.Add(new PhysicsSyncToEcsSystem());
        b.Add(new PhysicsCleanupSystem());
    }
}

// Pipeline construction
var pipeline = EcsPipeline.New()
    .AddModule(new PhysicsModule())
    // other modules
    .Build();
```

## Архитектура

```
┌─────────────────────────────────────────────────────────┐
│                     ECS (Dragon)                        │
├─────────────────────────────────────────────────────────┤
│  RigidBodyComponent  │  ColliderComponent  │  PhysicsState  │
└──────────┬────────────┴─────────┬───────────┴──────┬─────┘
           │                      │                   │
           ▼                      ▼                   ▼
┌─────────────────────────────────────────────────────────┐
│                   PhysicsSyncSystem                    │
├─────────────────────────────────────────────────────────┤
│                      IPhysicsWorld                       │
│  ┌──────────────────────────────────────────────────┐   │
│  │              AetherPhysicsWorld                  │   │
│  │              (реализация)                         │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

## Примеры использования

### Создание динамического тела с box коллайдером

```csharp
var entity = world.NewEntity();

ref var rigidBody = ref entity.Add<RigidBodyComponent>();
rigidBody.BodyType = RigidBodyType.Dynamic;
rigidBody.Mass = 1f;
rigidBody.LinearDamping = 0.01f;

ref var box = ref entity.Add<BoxColliderComponent>();
box.Size = new Vector2Float(1f, 1f);
box.Density = 1f;
box.Friction = 0.3f;

physicsWorld.CreateBody(entity, rigidBody, box);
```

### Создание circle коллайдера

```csharp
ref var circle = ref entity.Add<CircleColliderComponent>();
circle.Radius = 0.5f;
circle.Density = 1f;
circle.Friction = 0.3f;
```

### Использование Aspect для работы с разными коллайдерами

В Dragon ECS Aspect наследуются от `EcsAspect` и используют `B.IncludePool<T>()`.

```csharp
using DCFApixels.DragonECS;

// Aspect для работы с физическими телами
public sealed class PhysicsBodyAspect : EcsAspect
{
    public readonly EcsPool<RigidBodyComponent> RigidBodies = B.IncludePool<RigidBodyComponent>();
    public readonly EcsPool<PhysicsStateComponent> States = B.IncludePool<PhysicsStateComponent>();
}

// Aspect для box коллайдеров - комбинирование
public sealed class BoxColliderAspect : EcsAspect
{
    public readonly PhysicsBodyAspect PhysicsBody = B.Combine<PhysicsBodyAspect>();
    public readonly EcsPool<BoxColliderComponent> BoxColliders = B.IncludePool<BoxColliderComponent>();
}

// Aspect для circle коллайдеров
public sealed class CircleColliderAspect : EcsAspect
{
    public readonly PhysicsBodyAspect PhysicsBody = B.Combine<PhysicsBodyAspect>();
    public readonly EcsPool<CircleColliderComponent> CircleColliders = B.IncludePool<CircleColliderComponent>();
}
```

### Использование SystemAPI.Query (альтернатива Aspect)

```csharp
public partial struct VelocityDebugSystem : IEcsRun
{
    public void Run()
    {
        foreach (var (velocity, entity) in SystemAPI.Query<RefRO<RigidBodyComponent>>())
        {
            UnityEngine.Debug.Log($"Entity {entity}: velocity={velocity.ReadOnlyVal.Velocity}");
        }
    }
}
```

### Raycast

```csharp
var input = new RaycastInput
{
    Origin = playerPosition,
    Direction = direction,
    MaxDistance = 10f,
    Mask = CollisionCategory.Enemy | CollisionCategory.Wall
};

if (physicsWorld.Raycast(input, out var hit))
{
    // hit.Entity - что мы задели
}
```

### Trigger

```csharp
ref var box = ref entity.Add<BoxColliderComponent>();
box.Size = new Vector2Float(2f, 2f);
box.IsSensor = true;

// Обработка в системе
foreach (var (trigger, entity) in SystemAPI.Query<RefRO<BoxColliderComponent>>()
    .WithAll<IsTriggerEvent>())
{
    // Обработка входа/выхода
}
```

## Требования к реализации

1. **Zero Allocation**: Все временные данные в queries использовать Span/Memory
2. **Thread Safety**: IPhysicsWorld не потокобезопасен, синхронизация на уровне системы
3. **ECS Integration**: Полная интеграция с Dragon ECS
4. **Abstraction**: Aether деталь реализации, можно заменить на другой движок
