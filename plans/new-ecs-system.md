# New ECS System

**Purpose**: Create a new ECS system with proper Dragon ECS patterns

## Context

Working in **KarpikEngine** — a game engine with Dragon ECS architecture.

**Requirements:**
- Follow Dragon ECS patterns from `AGENTS.md`
- Use `EcsAspect` for filtering (preferred) or `IEcsInit` for caching pools
- Use `[DI]` for injecting dependencies (EcsDefaultWorld, Time, Application)
- Implement appropriate system interface: `IEcsRun`, `IEcsRunLate`, `IEcsRunParallel`, etc.

## Input

- **Component types**: What components does this system work with?
- **System logic**: What does this system do?
- **Run type**: When should it run? (IEcsRun, IEcsRunLate, etc.)

## Output

1. **Component files** (if new components needed):
   - Location: `Modules/Shared/ECS/` or appropriate module
   - Implement `IEcsComponent`
   - Use `struct`, not class

2. **System file**:
   - Location: Where the system should live
   - Use nested `EcsAspect` class with Inc/Exc/Opt pools
   - Use `EcsReadonlyPool` for read-only data

3. **Registration** (if needed):
   - How/where the system gets registered

## Constraints

- **DO NOT** use `new Component()` — use `pool.Add(entity) = new Component()`
- **DO NOT** inject pools directly with `[DI]` — get them from world or use Aspect
- **DO** use `EcsDefaultWorld` from `[DI]` to access the world
- **DO** use `foreach (var e in _world.Where(out Aspect a))` for Aspect iteration