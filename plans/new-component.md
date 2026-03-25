# New Component

**Purpose**: Create a new ECS component for Dragon ECS

## Context

Working in **KarpikEngine** — a game engine with Dragon ECS architecture.

**Requirements:**
- Component must implement `IEcsComponent`
- Use `struct`, not class
- Place in appropriate location (Shared, Client, or Server)

## Input

- **Component name**: What is the component called?
- **Fields**: What data does it hold?
- **Where is it used**: Client-only, Server-only, or Shared?

## Output

1. **Component file**:
   ```csharp
   public struct ComponentName : IEcsComponent
   {
       // fields
   }
   ```

2. **Location**:
   - `Modules/Shared/` — for shared components
   - `Modules/Client/` — for client-only
   - `Modules/Server/` — for server-only
   - `MyGame/Shared/` — for game-specific shared
   - `MyGame/Client/` or `MyGame/Server/` — for game-specific

## Constraints

- **MUST** implement `IEcsComponent`
- **MUST** be a `struct`, not a class
- **DO NOT** store entity references — use `entlong` instead
- **DO NOT** store references to managed objects
- Keep it simple — components are data containers
- If component needs initialization logic, consider `IEcsComponentLifecycle<T>`