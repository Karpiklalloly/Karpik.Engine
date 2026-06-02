# v0.4

## Key Changes
* **System Lifecycle:** Added an engine-owned deterministic pipeline: `Init -> Begin -> FixedUpdate -> Update -> LateUpdate -> Render -> Destroy`.
* **ECS API:** Added `DefaultWorld`, `EventWorld`, and `MetaWorld` facades, data-first component operations, and asynchronous lifecycle through `JobHandle`.
* **Hot Reload:** Moved reload to the restart-worker model. A new process restores ECS worlds, recreates runtime resources, and reconnects to the server correctly.
* **Module Graph:** Module dependencies now use a single `KarpikModuleDependency` item, are validated before startup, and expand into standard `ProjectReference` items for the IDE and compiler.
* **2D Rendering:** Replaced the Raylib backend with an OpenGL/Veldrid-like pipeline with command buffers, batching, camera support, textures, atlas fonts, and an ImGui overlay.
* **Physics2D:** Added a shared physics API, the `Physics2D.Aether2D` backend, ECS synchronization, and a platformer demo scene.

## Lifecycle and ECS
* Added `ISystemInit`, `ISystemBegin`, `ISystemFixedUpdate`, `ISystemUpdate`, `ISystemLateUpdate`, `ISystemRender`, and `ISystemDestroy`.
* `FixedUpdate` is used for physics and game simulation with fixed dt. Phases remain sequential in `v0.4`; scheduler-managed parallel execution is planned for `v0.5`.
* Added the preferred `DefaultWorld`, `EventWorld`, and `MetaWorld` facades.
* Added component add/remove operations with engine-owned lifecycle callbacks. Lifecycle errors include world, entity, component type, and phase diagnostics.
* Enabled `StaticAnalyzer` checks for raw Dragon lifecycle API usage and migration toward engine facades.
* Added tests for phase order, component lifecycle, and the ECS system dependency-graph prototype.

## Hot Reload
* Hot Reload now uses a separate worker process as the only reliable unload boundary for managed and native state.
* Only ECS state is persisted between workers. Services, graphics resources, sockets, and runtime handles are recreated after restart.
* Fixed client/server restoration: reconnect tokens, player entity rebinding, disconnected player cleanup, network cache restoration, and physics body recreation.
* Press `R` in the launcher console to reload either server or client manually. The client debug panel still exposes the `Hot Reload` button.

## Modules and Validation
* Projects under `Modules` and `MyGame` declare dependencies only through shorthand identifiers:

```xml
<KarpikModuleDependency Include="Physics2D" />
```

* Configurator derives side, plugin id, logical module id, and implementation from directory structure and `.csproj` names.
* Added structured `KarpikModuleSelection`, disabled/required dependency checks, cycle detection, side-leak validation, and stale generated-artifact checks.
* Added tracked `Generated/KarpikModuleCatalog.props`, allowing Rider and the compiler to resolve project references after reload.
* Installer initialization order is deterministic and teardown runs in reverse order.
* After adding, removing, or moving a project, run:

```powershell
dotnet run --project Configurator/Configurator.csproj -- --generate
dotnet run --project Configurator/Configurator.csproj -- --validate
```

## Graphics, Window, and Input
* Removed the `Graphics.Raylib` backend. Added `Graphics.OpenGL`, `Window.Core`, and `Window.Sdl2`.
* Added a 2D command-buffer renderer: `DrawRect`, `DrawTexture`, `DrawText`, batching, and a merge thread.
* Added `Camera2D`, camera-relative rendering, rotation, and configurable draw origin.
* Added texture, shader, and atlas SDF font loading through AssetManagement.
* Added an ImGui overlay for runtime diagnostics.
* Moved input handling to the SDL2 window/input source.

## Physics2D and Game Sample
* Added `Physics2D.Core` and the `Physics2D.Aether2D` implementation.
* Added ECS components for bodies, shapes, transforms, velocities, and collision layers.
* Added body creation, restoration, push/pull synchronization, and physics-step systems.
* Expanded the sample game into a platformer scene with platforms, player movement, a kinematic controller, and server-side input handling.

## Upgrade Notes
* User-facing ECS systems should migrate from Dragon `IEcs*` lifecycle APIs to the engine `ISystem*` interfaces.
* Do not add direct `ProjectReference` items under `Modules` or `MyGame`; use `KarpikModuleDependency`.
* Keep gameplay state that must survive Hot Reload in ECS components. Recreate process-local handles and runtime resources.
* The `v0.3` UI Toolkit has been removed from the runtime. A new UI API will be designed separately.
