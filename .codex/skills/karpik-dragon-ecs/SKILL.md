---
name: karpik-dragon-ecs
description: Dragon ECS rules for KarpikEngine. Use when Codex implements or reviews entities, IEcsComponent structs, EcsPool/EcsReadonlyPool access, EcsAspect filters, ECS systems, IEcsRun/IEcsInit/IEcsRunParallel, or ECS data layout.
---

# Karpik Dragon ECS

## Core Model
- Entity: `entlong`.
- Component: `struct` implementing `IEcsComponent`.
- System: logic implementing `IEcsRun`, `IEcsRunLate`, `IEcsRunParallel`, `IEcsInit` and related interfaces.
- World: `EcsWorld` / `EcsDefaultWorld`.
- Pools: `EcsPool<T>` for mutable data, `EcsReadonlyPool<T>` for read-only data.

## Components
Always:

```csharp
public struct Position : IEcsComponent
{
    public float X;
    public float Y;
}
```

Forbidden:

- classes for components;
- managed references in components;
- storing rich object graphs;
- static fields for gameplay state.

If a component needs to reference an entity, store `entlong`, not an object reference.

## Pool Access
Correct pattern:

```csharp
var pool = world.GetPool<Position>();
pool.Add(entity) = new Position { X = 10, Y = 5 };

if (pool.Has(entity))
{
    ref var pos = ref pool.Get(entity);
    pos.X += 1f;
}
```

Cache pools in `IEcsInit` when the system does not use an aspect or when that is locally cleaner.

## Preferred Filtering: EcsAspect
Prefer a nested `EcsAspect` for filtering:

```csharp
public class MovementSystem : IEcsRun
{
    private class Aspect : EcsAspect
    {
        public EcsPool<Position> Position = Inc;
        public EcsPool<Velocity> Velocity = Inc;
        public EcsReadonlyPool<StaticTag> Static = Opt;
    }

    [DI] private EcsDefaultWorld _world;

    public void Run()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            ref var pos = ref a.Position.Get(e);
            ref var vel = ref a.Velocity.Get(e);
            pos.X += vel.DX;
        }
    }
}
```

Rules:

- `Inc` - required components.
- `Exc` - excluded components.
- `Opt` - optional components.
- Read immutable data through `EcsReadonlyPool<T>`.
- Group related components into one aspect.

## Hot Path Rules
- Do not use LINQ or managed allocations inside `Run`.
- Do not call `GetPool<T>()` on every iteration.
- Do not keep component refs outside their safe usage scope.
- For large datasets, prefer linear traversal and dense data layout.
