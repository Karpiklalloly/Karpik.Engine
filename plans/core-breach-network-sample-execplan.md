# Build Core Breach as the KarpikEngine 1.0 network sample

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Build `Core Breach` as the first-party game sample that validates whether KarpikEngine is convenient enough to build a complete 2D client/server game by version 1.0.

The sample is a compact top-down arena defense game. A local authoritative server simulates a reactor core, player entities, enemies, bullets, pickups, waves, and turrets. One or more local clients connect to `localhost`, send input and build commands, receive snapshots, and render the current state. The sample is successful when a developer can run server and client, play a short wave-based loop, inspect debug counters, hot reload gameplay code, and use the codebase as the reference pattern for a small KarpikEngine game.

## Progress

- [x] (2026-06-08 00:00 +04:00) Initial plan created from the design discussion: first-party network sample, authoritative local server, no client prediction for the first version, and `Core Breach` arena-defense gameplay.
- [ ] Complete Milestone 1: repository orientation and sample naming decision.
- [ ] Complete Milestone 2: shared gameplay protocol and component model.
- [ ] Complete Milestone 3: authoritative server vertical slice.
- [ ] Complete Milestone 4: client input, snapshot application, and rendering vertical slice.
- [ ] Complete Milestone 5: wave loop, pickups, turret command, and HUD.
- [ ] Complete Milestone 6: hot reload, diagnostics, allocation checks, and sample documentation.
- [ ] Complete Milestone 7: 1.0 acceptance pass and ADR handoff.

## Surprises & Discoveries

- Observation: The existing `MyGame` already has Client / Server / Shared projects, command dispatch, reconnect tokens, generated snapshots, and local networking flow.
  Evidence: `MyGame/Shared/MyGame.Shared.Main/Components.cs`, `MyGame/Shared/MyGame.Shared.Main/NetworkManager.cs`, `MyGame/Server/MyGame.Server.Main/Systems/NetworkSystem.cs`, and `MyGame/Client/MyGame.Client.Main/MyGameClientInstaller.cs`.

- Observation: The current sample gameplay is platformer-shaped and includes platformer input and physics controller concepts that do not match the top-down arena sample.
  Evidence: `MyGame/Shared/MyGame.Shared.Main/PlatformerCommands.cs`, `MyGame/Shared/MyGame.Shared.Main/PlatformerComponents.cs`, `MyGame/Shared/MyGame.Shared.Main/KinematicCharacterController.cs`, and `MyGame/Server/MyGame.Server.Main/Systems/KinematicControllerSystem.cs`.

- Observation: The current server network system uses managed collections in update-time logic. That may be acceptable as transitional sample code, but the final 1.0 sample must either move these operations out of hot paths, preallocate them, or document an engine gap that needs fixing.
  Evidence: `MyGame/Server/MyGame.Server.Main/Systems/NetworkSystem.cs` uses `List<T>`, `Dictionary<TKey,TValue>`, `Queue<T>`, and `ToArray()` in runtime flow.

## Decision Log

- Decision: Build the sample as a first-party game in the repository, initially by evolving the existing `MyGame` project set instead of creating an external repository.
  Rationale: The goal is to validate the engine while it evolves. Keeping the sample in-tree makes Client / Server / Shared dependencies, generated network code, launcher flow, assets, and hot reload failures visible during engine development.
  Date/Author: 2026-06-08 / developer and agent

- Decision: Use an authoritative local server for the first version. The client sends commands only; it does not own movement, hits, enemy state, wave state, pickups, turret placement, or core HP.
  Rationale: This keeps the first network sample honest about side boundaries and avoids hiding protocol/API problems behind client prediction.
  Date/Author: 2026-06-08 / developer and agent

- Decision: Defer client-side prediction, rollback, procedural maps, complex AI, full editor tooling, and large metaprogression until after the 1.0 validation sample is stable.
  Rationale: These features would test advanced game design and netcode, not the core engine usability required for 1.0.
  Date/Author: 2026-06-08 / developer and agent

- Decision: Treat inconvenience in the sample as engine feedback, not as sample-local code to paper over.
  Rationale: The sample is a usability litmus test. If common tasks require unnatural boilerplate or allocations in hot paths, the engine API should be adjusted or the issue should be recorded.
  Date/Author: 2026-06-08 / developer and agent

- Decision: Keep early Core Breach work under the existing `MyGame/` project paths through the playable vertical slice. Name new gameplay files and types `CoreBreach*`, but defer folder, project, and namespace renaming until after milestones 1-5 or the 1.0 acceptance pass.
  Rationale: `MyGame` already contains the local client/server launch flow, generated snapshots, command dispatch, reconnect handling, installers, and resources. Renaming first would create project and generated-file churn before validating the actual gameplay and engine APIs.
  Date/Author: 2026-06-08 / developer and agent

- Decision: Use simple ECS radius-overlap collision for milestones 2-5 instead of integrating `Physics2D` into the first playable slice.
  Rationale: The first sample must validate client/server networking, ECS authoring, fixed tick, snapshots, commands, rendering, and hot reload. Pulling in the physics backend immediately would mix physics-module risk with the core network-game workflow. A later milestone may add a Physics2D variant if validating that module becomes the explicit goal.
  Date/Author: 2026-06-08 / developer and agent

- Decision: Use the existing generated snapshot pipeline for milestones 2-5 instead of writing a custom compact snapshot for the sample.
  Rationale: The sample should validate the engine's current `[NetworkedComponent]`, `[NetworkedField]`, `NetworkManager.WriteSnapshot`, and `NetworkManager.ApplySnapshot` workflow. If generated snapshots are too heavy, allocation-prone, or awkward for normal gameplay, that is engine feedback to record and address after the playable loop exists.
  Date/Author: 2026-06-08 / developer and agent

## Outcomes & Retrospective

No implementation outcome yet. Update this section after each milestone. At completion, record whether `Core Breach` proved the engine ready for 1.0 or identified blocking engine gaps.

## Context and Orientation

KarpikEngine is a 2D-first C# game engine with Dragon ECS, hot reload, Client / Server / Shared project boundaries, fixed-tick gameplay, networking, asset management, graphics, input, and diagnostics. The engine's real-time rule is no managed allocations in steady-state hot paths after warm-up.

The existing first-party sample lives under `MyGame/`:

- `MyGame/Shared/MyGame.Shared.Main` contains shared components, network commands, snapshot generation entry points, platformer controller data, and shared installer code.
- `MyGame/Server/MyGame.Server.Main` contains server module setup, command dispatch, authoritative network connection handling, local player creation, and platformer systems.
- `MyGame/Client/MyGame.Client.Main` contains client module setup, RPC sending, target RPC dispatching, input capture, rendering systems, local-player tagging, and asset presentation.
- `MyGame/MyGameResources` contains first-party content such as sprites, shaders, fonts, and mod sample content.
- `ClientLauncher/ClientLauncher.csproj` and `ServerLauncher/ServerLauncher.csproj` are the expected launchers for manual local validation.

This plan uses the name `Core Breach` for the game concept. Repository paths remain under `MyGame/` through the playable vertical slice. New gameplay types and files use `CoreBreach*` names inside the existing projects. A later milestone decides whether to rename folders, projects, and namespaces to `CoreBreach.*` before 1.0.

Terms:

- Authoritative server: the server owns gameplay truth. Client commands are requests, not facts.
- Snapshot: server-to-client serialized state used by the client to render replicated entities.
- Command: client-to-server serialized input or intent, such as movement, firing, aiming, or turret placement.
- Presentation component: client-only ECS data used for rendering, interpolation, animation, or effects. Presentation components do not affect authoritative gameplay.
- Reactor core: the defended objective at the center of the arena.
- Wave: a server-owned timed enemy spawn sequence.
- Litmus issue: any sample development friction that indicates an engine API, tooling, documentation, or performance problem.

## Real-Time Assessment

This work touches hot paths: client input update, server fixed gameplay simulation, ECS systems, command serialization, snapshot serialization, network pumps, render submission, and debug overlay updates.

Allocation budget:

- Final steady-state target is `0 B/frame` after warm-up for server simulation, client input command emission, snapshot application, replicated entity rendering, and diagnostics.
- Transitional managed collections may remain in early milestones only if they are recorded in `Surprises & Discoveries` and have a milestone-bound removal or engine follow-up.
- Client-only debug UI may allocate during early development, but 1.0 acceptance must identify whether debug overlay is sample-only or engine-supported no-GC diagnostics.

Data layout:

- Gameplay state must use Dragon ECS `struct` components implementing `IEcsComponent`.
- Components must not store managed object graphs.
- Entity links use `entlong`, `int` network IDs, or compact identifiers, not object references.
- Systems should use `EcsAspect`, cached pools, and linear traversal.

Side boundary:

- `Shared` may contain gameplay components, serializable protocol structs, deterministic rules, constants, and compact config structs.
- `Server` may contain authority, validation, spawn scheduling, damage, collisions, wave progression, reconnect/session management, and snapshot sending.
- `Client` may contain input capture, RPC sending, snapshot application glue, local presentation tags, camera, sprites, HUD, debug overlay, and effects.
- `Client` must not reference `Server`; `Server` must not reference `Client`; graphics/input types must not leak into `Shared`.

Tick behavior:

- Gameplay simulation uses fixed dt.
- Rendering and presentation may use frame dt.
- Server wave timers, cooldowns, projectile movement, enemy movement, damage windows, pickup lifetime, and turret cooldowns use fixed dt.

Networking:

- Frequent player input uses compact state commands and an explicit delivery method. Loss is acceptable for movement/aim state; event-like actions such as turret placement require reliable or sequence-validated delivery.
- Server-to-client snapshots use unreliable delivery unless the existing snapshot protocol requires otherwise.
- Milestones 2-5 use the existing generated snapshot path through `[NetworkedComponent]`, `[NetworkedField]`, `NetworkManager.WriteSnapshot`, and `NetworkManager.ApplySnapshot`.
- Only data required by client rendering, local-player binding, or HUD should be marked networked.
- Snapshot payload, allocation, or authoring friction is recorded as engine feedback before introducing a custom compact snapshot.
- RPC and snapshot payloads use structs and `IWriter` / `IReader`; classes, strings, arrays, and variable-length payloads are avoided in hot protocol paths.
- Server validates command source, target, player ownership, resource cost, placement distance, cooldown, and entity liveness.

Collision model:

- Milestones 2-5 use simple ECS radius overlap checks over `Position2D`, `Radius2D`, `Team`, `Damage`, `Health`, and gameplay tags.
- The first slice does not depend on `Physics2D` bodies, contacts, sensors, or backend-specific collision events.
- Radius overlap systems must use linear ECS traversal and preallocated scratch data if a temporary candidate list becomes necessary.
- Physics2D validation is explicitly deferred to a later milestone or a separate sample extension.

Concurrency:

- Current sample systems can remain sequential where networking or backend APIs require it.
- Future scheduler integration must mark systems with explicit read/write metadata or `[SequentialSystem]` as required by the `0.5` scheduler work.
- No blocking waits, locks, file I/O, or asset loads are allowed in frame, fixed update, render, or network receive hot paths.

Validation:

- Every implementation milestone ends with the smallest relevant build/test/manual validation.
- Targeted builds use single-node MSBuild with node reuse disabled: `dotnet build <project-or-solution> -m:1 -nr:false`.
- Network protocol tests must include serialization round trips for new commands and snapshots where the codegen/test surface supports it.
- Hot path changes require allocation checks or a recorded gap if the engine lacks allocation-budget tooling at that milestone.

## Plan of Work

The plan proceeds as a series of engine-validation milestones. Do not add later-stage gameplay polish until the current milestone proves the engine path is usable.

### Milestone 1: Repository orientation and sample naming decision

Goal: confirm that early work stays under `MyGame/` and establish the low-churn naming convention for Core Breach types.

Work:

1. Inspect current project references and generated files for `MyGame`:
   - `MyGame/Shared/MyGame.Shared.Main/MyGame.Shared.Main.csproj`
   - `MyGame/Server/MyGame.Server.Main/MyGame.Server.Main.csproj`
   - `MyGame/Client/MyGame.Client.Main/MyGame.Client.Main.csproj`
   - `Generated/`
   - `AutoGenerated.targets`
   - `KarpikEngine.slnx`
2. Keep paths and namespaces as `MyGame` for milestones 2-5.
3. Name new files and types `CoreBreach*` so the future rename is mostly mechanical.
4. Record any naming friction in `Surprises & Discoveries`, especially generated-code or documentation places where `MyGame` makes the sample harder to understand.
5. Do not create `CoreBreach/Shared`, `CoreBreach/Server`, `CoreBreach/Client`, or `CoreBreachResources` until the playable vertical slice proves the sample shape.

Validation:

- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet run --project Configurator/Configurator.csproj -- --validate`
- Expected observation: validation completes without Client / Server / Shared dependency leaks.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 -nr:false`
- Expected observation: client launcher builds.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 -nr:false`
- Expected observation: server launcher builds.

### Milestone 2: Shared gameplay protocol and component model

Goal: replace platformer-shaped shared concepts with arena-defense concepts while preserving snapshot and command generation.

Work:

1. In `MyGame/Shared/MyGame.Shared.Main`, split shared gameplay into focused files. If the conservative naming path is chosen, keep the namespace `Karpik.Engine.MyGame.Shared.Main` until the rename milestone.
2. Create or update shared component files:
   - `CoreBreachComponents.cs` for gameplay tags and scalar state:
     - `Player`
     - `ReactorCore`
     - `Enemy`
     - `Projectile`
     - `Turret`
     - `Pickup`
     - `Health`
     - `Energy`
     - `Damage`
     - `Lifetime`
     - `Cooldown`
     - `Team`
   - `CoreBreachTransformComponents.cs` for replicated 2D state:
     - `Position2D`
     - `Velocity2D`
     - `Facing2D`
     - `Radius2D`
   - `CoreBreachNetworkComponents.cs` for replicated IDs and session ownership if existing `NetworkId`, `PlayerSession`, and `PlayerConnection` are insufficient.
3. Create or update shared command files:
   - `CoreBreachInputCommand` as an `IStateCommand` containing movement axis, aim direction, fire held flag, and target network ID.
   - `PlaceTurretCommand` as an `IEventCommand` containing target position and target network ID.
4. Create shared constants/config structs:
   - player speed, projectile speed, projectile lifetime, core HP, enemy speed, enemy radius, turret cost, turret cooldown, pickup energy value, wave spawn intervals.
   - Keep them as simple structs/constants for the first version. Do not introduce a full content database in this milestone.
5. Delete or isolate platformer-only concepts from the active gameplay path:
   - `PlatformerInputCommand`
   - `KinematicCharacterController`
   - platformer collectibles, finish zones, death zones, respawn points, and platformer sprite assumptions.
6. Keep generated snapshot attributes only on data that the client must render or show in HUD.
7. Use the existing generated snapshot pipeline for replicated state. Do not introduce a hand-written compact snapshot format in this milestone.
8. Add tests for pure shared rules where possible:
   - turret placement cost validation;
   - cooldown tick behavior;
   - wave schedule math;
   - command serialization round trip if the current network test surface supports generated command serialization.

Validation:

- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet test ECS.Core.Tests/ECS.Core.Tests.csproj -m:1 -nr:false`
- Expected observation: existing ECS integration tests pass.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet run --project Configurator/Configurator.csproj -- --generate`
- Expected observation: generated module/network files update without errors.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet run --project Configurator/Configurator.csproj -- --validate`
- Expected observation: no invalid project references and no generated-file validation errors.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build MyGame/Shared/MyGame.Shared.Main/MyGame.Shared.Main.csproj -m:1 -nr:false`
- Expected observation: shared project builds without platformer gameplay dependencies.

### Milestone 3: Authoritative server vertical slice

Goal: the server can simulate one arena, one core, connected players, enemies, projectiles, damage, and snapshots without client-owned gameplay.

Work:

1. Keep or adapt `MyGame/Server/MyGame.Server.Main/Systems/NetworkSystem.cs` for connection lifecycle, reconnect token handling, local player assignment, command dispatch, and snapshot sending.
2. Remove platformer player creation from `NetworkSystem.CreatePlayer`:
   - no `KinematicCharacterController`;
   - no platformer physics body unless top-down collisions explicitly use Physics2D;
   - add top-down `Position2D`, `Velocity2D`, `Facing2D`, `Radius2D`, `Health`, `Energy`, `Player`, and `NetworkId`.
3. Add `ArenaInitSystem`:
   - creates a `ReactorCore` entity at arena center;
   - creates server-owned wave state;
   - creates static arena bounds if required by the collision approach.
4. Add `ServerPlayerCommandSystem`:
   - consumes `CoreBreachInputCommand`;
   - validates ownership through network ID and player session;
   - writes desired movement, aim, and fire state into ECS components.
5. Add `ServerMovementSystem`:
   - moves players, enemies, projectiles, and pickups using fixed dt;
   - clamps players to arena bounds;
   - avoids allocations and object lookups per entity.
6. Add `ServerWeaponSystem`:
   - handles fire cooldown;
   - spawns projectiles with `Projectile`, `Position2D`, `Velocity2D`, `Damage`, `Lifetime`, and `NetworkId`;
   - uses preconfigured values from shared constants.
7. Add `ServerEnemySystem`:
   - moves enemies toward the reactor core or nearest player according to the first-version rule;
   - keeps AI deterministic and local.
8. Add `ServerCollisionDamageSystem`:
   - checks projectile/enemy overlap and enemy/core overlap with simple radius tests over ECS components.
   - Do not create Physics2D bodies or subscribe to Physics2D contact events in the first playable slice.
9. Add `ServerLifetimeCleanupSystem`:
   - deletes expired projectiles and pickups;
   - queues destroyed network IDs through the existing network snapshot path.
10. Add `ServerWaveSystem`:
   - spawns a small number of enemies at fixed positions or simple ring positions;
   - increments wave when all enemies are dead.
11. Mark server systems `[SequentialSystem]` where existing networking or scheduler metadata cannot prove safety.
12. Record any no-GC blockers from existing network snapshot code in `Surprises & Discoveries`.

Validation:

- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build MyGame/Server/MyGame.Server.Main/MyGame.Server.Main.csproj -m:1 -nr:false`
- Expected observation: server game project builds.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 -nr:false`
- Expected observation: server launcher builds.
- Targeted test command when server tests exist:
  `dotnet test <server-test-project> -m:1 -nr:false`
- Expected observation: server movement, wave spawn, damage, and lifetime tests pass.
- Manual scenario:
  start `ServerLauncher`, let the server run without clients for 30 seconds, and observe no crash while core, wave state, and reconnect/session systems initialize correctly.

### Milestone 4: Client input, snapshot application, and rendering vertical slice

Goal: one client can connect to local server, send commands, receive snapshots, render the arena, and control an authoritative player.

Work:

1. Adapt `MyGame/Client/MyGame.Client.Main/Systems/InputSystem.cs`:
   - capture WASD movement as a normalized 2D vector;
   - capture mouse aim as a direction or world target position;
   - capture fire held state;
   - capture turret-placement request as a discrete command;
   - send `CoreBreachInputCommand` at a controlled rate, not as unbounded per-frame event spam;
   - send `PlaceTurretCommand` only on input edge.
2. Keep the client from changing authoritative gameplay components directly.
3. Adapt target RPC dispatch so `SetLocalPlayerTargetRpc` tags the replicated local player entity with `LocalPlayer`.
4. Add or update render/presentation systems:
   - render core, player, enemies, projectiles, pickups, and turrets from replicated components;
   - use client-only presentation data for sprite/color/size mapping;
   - do not require server-side graphics or input types.
5. Use replicated generated snapshot state as the source for renderable gameplay entities. Do not add a parallel manual snapshot reader for Core Breach unless this plan records a generated snapshot blocker first.
6. Add a simple camera:
   - fixed arena camera for the first slice, or follow local player if that is already easy with existing camera APIs.
7. Add client debug overlay:
   - connection state;
   - local player network ID;
   - snapshot count;
   - visible replicated entity count.
8. Remove or isolate platformer-only draw/input systems from active client setup.

Validation:

- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build MyGame/Client/MyGame.Client.Main/MyGame.Client.Main.csproj -m:1 -nr:false`
- Expected observation: client game project builds.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 -nr:false`
- Expected observation: client launcher builds.
- Manual scenario:
  start `ServerLauncher`, start `ClientLauncher`, connect to local server, move the player, fire projectiles, observe replicated enemies and core state.
- Expected observation:
  the client does not crash if one snapshot is dropped; the next snapshot restores render state.

### Milestone 5: Wave loop, pickups, turret command, and HUD

Goal: the sample becomes a short playable loop that validates content authoring and common gameplay iteration.

Work:

1. Add wave rules:
   - wave 1: slow enemies;
   - wave 2: more enemies;
   - wave 3: one tougher enemy variant.
2. Add enemy variants through data, not separate class hierarchies:
   - small fast enemy;
   - medium baseline enemy;
   - tough slow enemy.
3. Add pickups:
   - enemies drop energy pickups server-side;
   - player pickup overlap awards energy;
   - pickups are replicated until collected or expired.
4. Add turret placement:
   - client sends `PlaceTurretCommand`;
   - server validates player ownership, energy cost, placement distance, arena bounds, and no overlap with core;
   - server creates turret entity and subtracts energy.
5. Add turret behavior:
   - target nearest enemy within radius;
   - fire projectiles on cooldown;
   - no managed allocations in target selection.
6. Add HUD:
   - reactor HP;
   - player HP;
   - energy;
   - wave number;
   - enemy count;
   - connection state.
7. Add simple win/loss:
   - loss when reactor HP reaches zero;
   - win after completing the configured final wave.
8. Add content iteration notes:
   - where to change wave values;
   - where to change weapon/turret values;
   - how hot reload should preserve or reset ECS state.

Validation:

- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet run --project Configurator/Configurator.csproj -- --validate`
- Expected observation: no side leaks after gameplay expansion.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 -nr:false`
- Expected observation: client launcher builds.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 -nr:false`
- Expected observation: server launcher builds.
- Manual scenario:
  play from wave 1 through win or loss.
- Expected observation:
  player can move, shoot, collect energy, place a turret, see HUD changes, and finish the short loop without restarting either process.

### Milestone 6: Hot reload, diagnostics, allocation checks, and sample documentation

Goal: prove the sample is useful as an engine development litmus test, not just as a working arena game.

Work:

1. Define hot reload expectations:
   - ECS gameplay state survives worker restart where the engine already supports it;
   - runtime resources, network sockets, graphics resources, and process-local handles are recreated;
   - client and server can be reloaded independently.
2. Add manual hot reload scenarios:
   - change weapon cooldown;
   - change enemy speed;
   - change wave spawn interval;
   - reload server while a client is connected;
   - reload client while the server continues running.
3. Add diagnostics visible in client or server logs:
   - server fixed tick;
   - connected peers;
   - replicated entity count;
   - snapshot bytes per second if available;
   - command packets per second if available;
   - destroyed network IDs queued per snapshot;
   - allocation counter if the engine has a stable API.
4. Add allocation checks:
   - identify which tests or benchmark projects should measure steady-state command send, snapshot write/apply, and server simulation;
   - if the engine lacks stable allocation-budget tests, add a `Surprises & Discoveries` entry and a follow-up plan instead of pretending the sample is verified.
5. Add docs:
   - `docs/knowledge/patterns/core-breach-sample-architecture.md` for compact reusable sample patterns after implementation evidence exists;
   - update README or docs index only after the sample is playable.
6. Decide whether to create an ADR:
   - create an ADR only if the sample locks in a durable networking authority model, snapshot model, or Client / Server / Shared sample structure.

Validation:

- Manual scenario:
  run client and server, play for at least 2 minutes, hot reload server, continue playing, hot reload client, continue playing.
- Expected observation:
  no duplicate players, no lost local-player assignment after reconnect/reload, and no stale process-local resources.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet test KarpikEngine.slnx -m:1 -nr:false`
- Expected observation:
  relevant unit and integration tests pass. If full solution test time is too high during active development, record the targeted commands that passed and run the full command at release gate.

### Milestone 7: 1.0 acceptance pass and ADR handoff

Goal: make the sample shippable as the 1.0 reference game.

Work:

1. Decide whether to rename `MyGame` to `CoreBreach`:
   - if the sample remains first-party and public-facing, use clear `CoreBreach` paths/namespaces before 1.0;
   - if `MyGame` is retained as a generic template, split `CoreBreach` into its own first-party sample folder.
2. Remove unused platformer code from the active sample path or move it to a separate legacy sample if it still has value.
3. Ensure all sample systems are either scheduler-safe or explicitly sequential.
4. Ensure common authoring tasks are documented:
   - add enemy;
   - add weapon;
   - add pickup;
   - add turret;
   - add network command;
   - add replicated component;
   - add HUD value.
5. Run side-boundary, build, test, and manual validation gates.
6. Update roadmap documents to state what the sample proves for 1.0 and what remains post-1.0.
7. Create or update ADRs for durable architecture decisions discovered during the sample.

Validation:

- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet run --project Configurator/Configurator.csproj -- --generate`
- Expected observation:
  generated files are current.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet run --project Configurator/Configurator.csproj -- --validate`
- Expected observation:
  no Client / Server / Shared violations.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 -nr:false`
- Expected observation:
  server launcher builds.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 -nr:false`
- Expected observation:
  client launcher builds.
- Command from `C:\Users\artem\RiderProjects\KarpikEngine`:
  `dotnet test KarpikEngine.slnx -m:1 -nr:false`
- Expected observation:
  tests pass or this plan records exact failing tests with owner/follow-up before release.
- Manual acceptance:
  launch local server and client, complete a short game loop, trigger win or loss, hot reload at least once, reconnect client, and verify HUD/debug counters remain coherent.

## Milestones

Milestone 1 is complete when the low-churn `MyGame` path decision is recorded and current launchers still build.

Milestone 2 is complete when shared arena-defense components and commands replace the active platformer protocol and generated validation succeeds.

Milestone 3 is complete when the server can run an arena simulation and produce snapshots without a connected client.

Milestone 4 is complete when one client can connect to the local server, move an authoritative player, fire, and render snapshots.

Milestone 5 is complete when the short wave loop is playable: enemies attack the core, the player shoots, pickups grant energy, turrets can be placed, and win/loss is visible.

Milestone 6 is complete when the sample provides useful engine feedback: hot reload scenarios, diagnostics, allocation checks or recorded allocation-tooling gaps, and concise architecture notes.

Milestone 7 is complete when `Core Breach` is ready to serve as the KarpikEngine 1.0 reference sample with clean naming, docs, validation, and ADR handoff.

## Concrete Steps

Use these commands from `C:\Users\artem\RiderProjects\KarpikEngine` unless a step says otherwise.

Inspect project shape:

```powershell
Get-ChildItem -Recurse -Depth 3 -Path MyGame | Select-Object FullName,Mode,Length
rg -n "Platformer|KinematicCharacter|NetworkedComponent|IStateCommand|IEventCommand|SetLocalPlayer" MyGame -g *.cs
```

Validate module graph:

```powershell
dotnet run --project Configurator/Configurator.csproj -- --validate
```

Regenerate generated project/network files after changing module dependencies, networked components, or commands:

```powershell
dotnet run --project Configurator/Configurator.csproj -- --generate
```

Build the shared project:

```powershell
dotnet build MyGame/Shared/MyGame.Shared.Main/MyGame.Shared.Main.csproj -m:1 -nr:false
```

Build the server game project and launcher:

```powershell
dotnet build MyGame/Server/MyGame.Server.Main/MyGame.Server.Main.csproj -m:1 -nr:false
dotnet build ServerLauncher/ServerLauncher.csproj -m:1 -nr:false
```

Build the client game project and launcher:

```powershell
dotnet build MyGame/Client/MyGame.Client.Main/MyGame.Client.Main.csproj -m:1 -nr:false
dotnet build ClientLauncher/ClientLauncher.csproj -m:1 -nr:false
```

Run available targeted tests:

```powershell
dotnet test ECS.Core.Tests/ECS.Core.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
```

Run full validation at release gates:

```powershell
dotnet test KarpikEngine.slnx -m:1 -nr:false
```

Manual local server/client scenario:

```powershell
dotnet run --project ServerLauncher/ServerLauncher.csproj
dotnet run --project ClientLauncher/ClientLauncher.csproj
```

Expected manual observations:

- server accepts a local client;
- client receives a local player assignment;
- movement and fire commands affect server-owned state;
- snapshots update client rendering;
- reactor HP, wave number, energy, and entity counts are visible;
- disconnect/reconnect does not duplicate the player entity;
- hot reload does not lose durable ECS gameplay state.

## Validation and Acceptance

The final sample is accepted for KarpikEngine 1.0 when all of these are true:

- A developer can start local server and client from the documented launchers.
- The client never owns authoritative gameplay state.
- The server owns player movement, enemy movement, wave state, projectile hits, pickups, turret placement, core HP, win, and loss.
- Shared protocol and gameplay structs do not reference client graphics/input or server-only types.
- Common gameplay state is stored in ECS `struct` components.
- The sample has no intentional managed allocations in steady-state fixed update, network serialization, snapshot application, render submission, or input command hot paths after warm-up.
- Any remaining allocation or API friction is recorded as an engine issue, ADR, or follow-up ExecPlan.
- The sample includes enough documentation for a new developer to add one enemy, one weapon, one pickup, one turret, one command, and one replicated component.
- Configurator validation passes.
- Client and server launchers build with `-m:1 -nr:false`.
- Relevant tests pass, or the plan records exact failures and release-blocking owners.
- Manual playthrough from launch to win/loss succeeds.
- Server and client hot reload scenarios are tested and recorded.

## Idempotence and Recovery

Configurator `--generate` and `--validate` are safe to rerun after every project, dependency, command, or networked component change.

Build and test commands are safe to rerun. Use single-node builds with MSBuild node reuse disabled to avoid accumulating idle `dotnet.exe` build nodes.

If a rename from `MyGame` to `CoreBreach` fails, recover by reverting only the rename-related files changed in that milestone. Do not revert unrelated engine work. Re-run configurator generation and validation before continuing.

If network codegen fails after adding a command or networked component, reduce the change to one command/component, regenerate, and inspect generated code before adding the next type.

If a hot reload scenario corrupts ECS state, stop implementation and update `Surprises & Discoveries` with:

- exact reload direction: client, server, or both;
- current game state before reload;
- expected surviving ECS components;
- actual missing, duplicated, or stale components;
- command used to reproduce.

If allocation checks fail, keep the gameplay behavior but mark the milestone incomplete until the allocation source is either removed, moved out of the hot path, or recorded as a separate engine blocker.

## Artifacts and Notes

Primary plan file:

- `plans/core-breach-network-sample-execplan.md`

Likely implementation areas:

- `MyGame/Shared/MyGame.Shared.Main`
- `MyGame/Server/MyGame.Server.Main`
- `MyGame/Client/MyGame.Client.Main`
- `MyGame/MyGameResources`
- `Generated/`
- `ClientLauncher/ClientLauncher.csproj`
- `ServerLauncher/ServerLauncher.csproj`

Potential follow-up documents after evidence exists:

- `docs/02_ADR/core-breach-authoritative-network-sample.md`
- `docs/knowledge/patterns/core-breach-sample-architecture.md`
- `docs/04_Roadmap/karpikengine-1.0-roadmap.md`

Do not create the follow-up ADR or knowledge note until implementation evidence confirms the final design.
