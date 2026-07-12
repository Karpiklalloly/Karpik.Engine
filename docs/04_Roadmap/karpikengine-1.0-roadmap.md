# KarpikEngine Roadmap 1.0

KarpikEngine - 2D-first C# game engine/framework с ECS/data-oriented архитектурой, hot reload, no-GC hot paths и Client/Server/Shared разделением.

MonoGame используется как ориентир покрытия возможностей, но не как цель API compatibility.

## Главная цель 1.0

Сделать стабильный 2D runtime/framework, на котором можно написать полноценную 2D игру без постоянного обхода ограничений движка.

Фокус 1.0:

- сильный 2D runtime;
- ECS-first архитектура;
- hot reload;
- no-GC hot paths после warm-up;
- удобный renderer;
- asset pipeline;
- input/audio/tilemap/animation;
- базовый networking/replay sample;
- хорошие diagnostics/tools;
- понятные templates, samples и docs.

## Не входит в 1.0

- полноценный визуальный редактор уровня Godot/Unity;
- 3D;
- сложная 2D lighting pipeline с тенями;
- полноценная UI-система уровня WPF/React;
- полноценный modding sandbox;
- behavior tree / utility AI runtime;
- advanced rollback multiplayer как production-ready решение;
- MonoGame API compatibility.

Для этих направлений 1.0 должен дать foundation/sample там, где это полезно, а production-версии вынести post-1.0.

## Архитектурные правила

### Runtime State

- Gameplay/runtime state должен храниться в ECS `struct` components.
- ECS components не должны содержать managed object graph.
- Services могут хранить resources, caches, indexes, transient buffers и platform/backend objects.
- Services не должны хранить authoritative gameplay state.

### No-GC Hot Paths

После warm-up не должны аллоцировать:

- `Update`;
- fixed simulation;
- ECS systems;
- render submission;
- input update;
- asset lookup;
- network serialization/snapshots;
- jobs scheduling;
- audio update;
- tilemap rendering;
- steady-state UI/debug overlay.

### Client / Server / Shared

- Client не должен ссылаться на Server.
- Server не должен ссылаться на Client.
- Shared содержит общие components, protocol, deterministic simulation logic, shared gameplay rules и DTO/serialization types.

### Time

- Physics/gameplay/network simulation используют fixed dt.
- Rendering может быть frame-based.
- Должны быть отдельные фазы для fixed update, variable update, render submit и late/update end hooks.

## 0.4 Runtime Foundation

Цель: закрепить фундамент исполнения: lifecycle, modules, validation, hot reload rules.

### System Lifecycle API

- [x] Добавить и стабилизировать:
  - [x] `ISystemInit`;
  - [x] `ISystemBegin`;
  - [x] `ISystemFixedUpdate`;
  - [x] `ISystemUpdate`;
  - [x] `ISystemLateUpdate`;
  - [x] `ISystemRender`;
  - [x] `ISystemDestroy`.
- [x] Зафиксировать порядок `Init -> Begin -> FixedUpdate -> Update -> LateUpdate -> Render -> Destroy`.
- [x] Оставить выполнение фаз последовательным в `0.4`.
- [x] Сохранить Dragon lifecycle API только как compatibility/backend слой.
- [x] Покрыть phase order smoke-тестом.

System dependency graph для публичных `ISystem*` перенесён в `0.5`, где он проектируется вместе со scheduler, thread affinity и jobs.

### ECS State Rules

- [x] Добавить preferred world facades: `DefaultWorld`, `EventWorld`, `MetaWorld`.
- [x] Добавить data-first component lifecycle через `JobHandle`.
- [x] Добавить lifecycle diagnostics с world, entity, component type и phase.
- [x] Проверять lifecycle bypass и raw Dragon lifecycle API анализатором.
- [x] Документировать ECS-only hot reload state boundary.
- [x] Документировать raw-world compatibility/backend escape hatch.
- [ ] Добавить запрет/предупреждение для managed references в runtime components. Follow-up: `0.4.x`.

### Validation

- [x] Проверять Client/Server/Shared project-reference boundaries через `Configurator --validate`.
- [x] Запускать validation при сборке launcher/publish проектов.
- [x] Подключить `StaticAnalyzer` к `Modules` и `MyGame`.
- [x] Разрешить `IEcsRunOnEvent<T>` до появления Karpik event-system API.
- [x] Покрыть scheduler prototype graph и world lifecycle тестами.
- [x] Подключить test-проекты к основному `.slnx`.

### Smoke

- [x] Client стартует и рендерит sample.
- [x] Server использует fixed tick.
- [x] Restart-worker hot reload сохраняет и восстанавливает ECS worlds.
- [x] Lifecycle-тесты запускаются через `dotnet test KarpikEngine.slnx`.

### Done Criteria

- [x] Порядок lifecycle-фаз предсказуем.
- [x] Lifecycle стабилен.
- [x] Client/Server/Shared boundaries проверяются.
- [x] Hot reload state rules задокументированы.
- [x] Есть минимальные тесты lifecycle и scheduler prototype graph.

## 0.4.x Module Graph Follow-Up

Цель: развивать module graph независимо от закрытого lifecycle foundation и не блокировать переход к scheduler/jobs работе.

### Module Metadata

- [x] Выводить side, plugin id, logical module id и implementation из структуры каталогов и имен `.csproj`.
- [x] Хранить enabled/disabled и выбранную implementation в структурированных `KarpikModuleSelection`.
- [x] Использовать `KarpikModuleDependency` как единственный source-level project dependency item в `Modules` и `MyGame`.
- [x] Статически преобразовывать `KarpikModuleDependency` в MSBuild `ProjectReference`, чтобы IDE сразу видела типы.
- [x] Поддержать required/optional runtime dependencies.
- [x] Добавить configuration schema для build-time validation без runtime binding.

### Module Validation

- [x] Валидировать module graph:
  - [x] missing dependencies;
  - [x] required/disabled conflicts;
  - [x] circular dependencies.
- [x] Валидировать conventions и запрещать прямой source-level `ProjectReference` в `Modules` и `MyGame`.
- [x] Строить deterministic topo-order загрузки DLL для Client и Server.
- [x] Оставить `[Module(priority)]` для installer lifecycle, добавить deterministic tie-break и reverse destroy order.
- [x] Выдавать clear errors before runtime.
- [x] Хранить generated manifest внутри `Generated/ModuleLoader.cs`.
- [x] Покрыть module graph тестами.

Подробный план: [`plans/module-graph-execplan.md`](../../plans/module-graph-execplan.md).

## 0.5 Scheduler / Jobs / Memory

> Source of truth for the accepted `0.5` architecture and implementation order:
> [`plans/scheduler-jobs-memory-execplan.md`](../../plans/scheduler-jobs-memory-execplan.md).
>
> The release is implemented through four ordered child ExecPlans:
> [`native-memory-foundation-execplan.md`](../../plans/native-memory-foundation-execplan.md),
> [`jobs-runtime-execplan.md`](../../plans/jobs-runtime-execplan.md),
> [`ecs-update-scheduler-execplan.md`](../../plans/ecs-update-scheduler-execplan.md), and
> [`client-threading-render-pipeline-execplan.md`](../../plans/client-threading-render-pipeline-execplan.md).
>
> Accepted scope clarification: worker scheduling in `0.5` applies to `ISystemUpdate` and read-only
> `ISystemRenderPrepare`. `ISystemFixedUpdate` remains sequential. Client rendering reuses the existing
> Graphics.Core `DrawCommand` / `ThreadBuffer` / `MergeThread` pipeline and extends it with sort keys and
> triple-buffer ownership.

Цель: сделать безопасную основу для parallel ECS systems и no-GC job execution.

### ECS Access Metadata

Для 1.0 нужна практичная система, не "магический идеальный analyzer".

- [x] Поддержать явные атрибуты:
  - [x] `[Reads<T>]`
  - [x] `[Writes<T>]`
  - [x] `[Reads(typeof(T))]`
  - [x] `[Writes(typeof(T))]`
  - [x] `[RunsAfter<TSystem>]`
  - [x] `[RunsBefore<TSystem>]`
- [x] Analyzer-lite должен проверять простые случаи:
  - [x] system получает `EcsPool<T>` -> write;
  - [x] system получает `EcsReadonlyPool<T>` -> read;
  - [x] aspect fields;
  - [x] known helper wrappers.
Analyzer в 1.0 fail-closed для сложного control flow, reflection, indirect calls,
runtime-generated access и aliasing через generic abstractions.
- [x] Добавить conservative fallback:
  - [x] unresolved access = conflict;
  - [x] explicit override attributes;
  - [x] diagnostics;
  - [x] ability to disable parallelization for system.
Control-flow-aware analysis — post-1.0.

### Scheduler

- [x] Заменить изолированный прототип `IEcsRunParallel` scheduler backend-ом для `ISystemUpdate`.
- [x] Определить client runtime threading model:
  - [x] вызывать main-thread phases на OS/main thread;
  - [x] выполнять simulation phases на выделенном simulation thread;
  - [x] оставить platform/input часть `Begin` и submit/present часть `Render` на main thread;
  - [x] передавать input через bounded SPSC ring/latest-state boundary и render commands через triple-buffered command sets;
  - [x] добавить явные barriers для shutdown и hot reload.
Platform, ImGui и render остаются в dedicated main-thread lifecycle phases; они не dispatch-ятся обратно
из `ISystemUpdate`. `ISystemUpdate` выполняется на simulation worker и проверяется analyzer-ом.
- [x] Использовать generated/manual read-write metadata.
- [x] Строить safe parallel groups.
- [x] Валидировать read/write conflicts.
- [x] Учитывать `RunAfter` / `RunBefore`.
- [x] Поддержать cycle detection для explicit ordering.
- [x] Добавить diagnostics for invalid order.
- [x] `ISystemFixedUpdate` остаётся последовательным и deterministic; parallel fixed groups — post-1.0.
- [x] Поддержать update phase groups.
- [x] Поддержать render-prepare phase groups.
- [x] Добавить single-thread fallback mode.
- [x] Добавить deterministic scheduling option.

### Karpik.Jobs

- [x] Стабилизировать как отдельный submodule/product:
  - [x] public API;
  - [x] standalone tests;
  - [x] benchmarks;
  - [x] docs;
  - [x] standalone usage examples.
- [x] Обязательные features к 1.0:
  - [x] job handles;
  - [x] dependencies;
  - [x] worker pool;
  - [x] clean shutdown;
  - [x] no-GC scheduling after warm-up;
  - [x] simple work stealing or equivalent balancing;
  - [x] exception handling/reporting;
  - [x] profiler hooks.
- [ ] Необязательно к 1.0:
  - [ ] optimized cancellation path for standalone value jobs;
  - [ ] сложные scheduling heuristics;
  - [ ] fiber-like execution;
  - [ ] custom task graph editor.

### Memory

- [x] Добавить unmanaged allocators:
  - [x] arena allocator;
  - [x] linear allocator;
  - [x] pool allocator;
  - [x] explicit lifetime/dispose;
  - [x] `Span<T>` / ref-friendly wrappers;
  - [x] leak diagnostics;
  - [x] double-dispose checks where practical;
  - [x] debug mode validation.

Allocation gates для runtime subsystems ведутся в их собственных release sections; они не блокируют 0.5.

### Done Criteria

- [x] Есть safe parallel ECS execution.
- [x] Можно отключить parallel execution.
- [x] No-GC scheduling после warm-up.
- [x] Есть diagnostics конфликтов.
- [x] Jobs можно использовать отдельно.
- [x] Есть базовые benchmarks.

## 0.6 2D Runtime Core

Цель: сделать удобное и стабильное 2D ядро: renderer, camera, input, content pipeline.

### 2D Math

- [ ] Провести investigation текущей Physics2D backend/libraries на поддержку `double`.
- [ ] В runtime использовать `double` для:
  - [ ] transforms;
  - [ ] camera;
  - [ ] physics coordinates, если backend позволяет без непропорционального rewrite;
  - [ ] tilemaps;
  - [ ] scenes/prefabs;
  - [ ] networking coordinates.
- [ ] Render boundary:
  - [ ] camera-relative `double -> float`;
  - [ ] минимизация precision issues;
  - [ ] documented coordinate rules.
- [ ] Если Physics2D backend остается float-based в 1.0, явно задокументировать boundary и вынести full-double physics в post-1.0.

### Renderer Facade

- [ ] Добавить public `IRenderer` поверх command-buffer renderer.
- [ ] Низкоуровневые command buffers оставить доступными.

### Renderer API

- [ ] Поддержать:
  - [ ] `DrawTexture`;
  - [ ] `DrawSprite`;
  - [ ] `DrawRect`;
  - [ ] `DrawText`;
  - [ ] `DrawLine`;
  - [ ] базовые primitives;
  - [ ] source rect / UV;
  - [ ] origin;
  - [ ] rotation;
  - [ ] scale;
  - [ ] flip;
  - [ ] tint/alpha;
  - [ ] screen space;
  - [ ] world space;
  - [ ] layer depth;
  - [ ] sorting;
  - [ ] sprite atlases;
  - [ ] render targets;
  - [ ] blend modes;
  - [ ] sampler modes;
  - [ ] viewport;
  - [ ] scissor;
  - [ ] batching diagnostics.

### Renderer Diagnostics

- [ ] Draw call count.
- [ ] Batch count.
- [ ] Texture switches.
- [ ] Shader/pipeline switches.
- [ ] Render target switches.
- [ ] Submitted quads.
- [ ] Text glyph count.
- [ ] Debug overlay through ImGui.

### Build-time Sprite Atlases

- [ ] Add a build-time atlas pipeline for 2D sprites when the game has a real multi-texture workload.
- [ ] Keep strict painter order as the default rendering rule; atlas usage must reduce texture switches without reordering transparent commands.
- [ ] Store atlas manifests with source content; generate atlas images and UV metadata under build intermediates rather than modifying source assets.
- [ ] Make Debug and Release consume the same generated atlas output, replacing the Debug-only direct Content junction where required.
- [ ] Expose atlas regions through the existing texture/sprite API as a texture plus source UV rectangle.
- [ ] Handle padding, bleeding protection, maximum atlas size, multiple atlas pages, and deterministic packing.
- [ ] Add build validation and a sample that proves multiple sprites from one atlas render in strict draw order with one resource set.
- [ ] Do not start this work until content contains enough distinct sprites/textures for texture switches to be a measured bottleneck; current texture-thrash diagnostics are intentionally synthetic.

### Camera2D

- [x] Position.
- [x] Zoom.
- [x] Rotation.
- [x] Viewport.
- [x] Screen-to-world.
- [x] World-to-screen.
- [ ] Camera-relative rendering.
- [ ] Multiple cameras where practical.

### Text Rendering

- [ ] Atlas fonts.
- [ ] SDF/MSDF fonts.
- [ ] Glyph caching rules.
- [ ] Text layout basics.
- [ ] No-GC steady-state rendering.
- [ ] Fallback glyph diagnostics.

### Content Pipeline Base

- [ ] Asset manifest.
- [ ] Typed asset handles.
- [ ] Asset dependency graph.
- [ ] Stable asset IDs/references needed by scenes/prefabs.
- [ ] Processors for:
  - [ ] textures;
  - [ ] fonts;
  - [ ] shaders;
  - [ ] data/json;
  - [ ] tilemaps.
- [ ] CLI command for asset build.
- [ ] Asset validation.
- [ ] Hot reload notification.
- [ ] Asset dependency invalidation.

### Input

- [x] Перевести input module на no-GC snapshot API.
- [ ] Поддержать:
  - [x] keyboard held/pressed/released;
  - [x] mouse position;
  - [x] mouse delta;
  - [x] mouse wheel;
  - [x] mouse buttons;
  - [x] text input separately from key input;
  - [ ] gamepad buttons;
  - [ ] gamepad sticks;
  - [ ] gamepad triggers;
  - [ ] gamepad connection/disconnection;
  - [ ] optional touch abstraction;
  - [x] input capture integration for UI/debug overlay.

### Input Action / Remapping

- [ ] Basic version:
  - [ ] actions;
  - [ ] axes;
  - [ ] bindings;
  - [ ] profiles;
  - [ ] runtime rebinding;
  - [ ] persistence through config/assets.
- [ ] Не делать в 1.0:
  - [ ] сложный visual input editor;
  - [ ] Steam Input-level abstraction.

### Foundation Utilities

- [ ] Add virtual file system foundation:
  - [ ] mounted asset folders;
  - [ ] user data folder;
  - [ ] future mod mount points;
  - [ ] path normalization.
- [ ] Add save/config system:
  - [ ] save files;
  - [ ] game settings;
  - [ ] window/audio settings;
  - [ ] input profiles;
  - [ ] versioned data format.
- [ ] Add debug draw API:
  - [ ] lines;
  - [ ] rectangles/circles;
  - [ ] collider debug;
  - [ ] tile grid debug;
  - [ ] camera/debug overlay integration.

### Done Criteria

- [ ] Можно сделать 2D игру с camera/sprites/text/input.
- [ ] Renderer удобнее прямого command buffer.
- [ ] Есть typed asset handles и stable references.
- [ ] Input не аллоцирует в steady state.
- [ ] Есть renderer diagnostics.
- [ ] Есть save/config foundation.
- [ ] Есть примеры renderer/camera/text/input.

## 0.7 Authoring Content

Цель: сделать нормальный workflow для сцен, prefabs, tilemaps и audio.

### Scenes

- [ ] Asset-based scenes:
  - [ ] scene assets create ECS entities/components;
  - [ ] references through typed asset handles/stable asset IDs;
  - [ ] validation;
  - [ ] load/unload;
  - [ ] additive loading where practical;
  - [ ] hot reload where practical.

### Prefabs

- [ ] Asset-based prefabs:
  - [ ] prefab assets create ECS entities/components;
  - [ ] nesting;
  - [ ] overrides;
  - [ ] references through stable IDs;
  - [ ] validation;
  - [ ] runtime instantiate;
  - [ ] no-GC instantiate path where practical after warm-up/cache.

### Tilemaps

- [ ] Production tilemap workflow:
  - [ ] chunked renderer;
  - [ ] camera culling;
  - [ ] no-GC draw submission;
  - [ ] collision layers;
  - [ ] tile metadata;
  - [ ] runtime edits;
  - [ ] save/load;
  - [ ] Tiled import;
  - [ ] LDtk import;
  - [ ] multiple layers;
  - [ ] animated tiles;
  - [ ] autotiling/rule tiles basic;
  - [ ] parallax;
  - [ ] hot reload.

### Audio.Core

- [ ] Client-only audio module.
- [ ] Short sound effects.
- [ ] Sound instances.
- [ ] Play/stop/pause/loop.
- [ ] Volume.
- [ ] Pitch.
- [ ] Pan.
- [ ] Streamed music.
- [ ] Mixer/buses:
  - [ ] master;
  - [ ] music;
  - [ ] sfx;
  - [ ] ui.
- [ ] Audio asset processing.
- [ ] Diagnostics:
  - [ ] active voices;
  - [ ] dropped sounds;
  - [ ] stream state.

### Done Criteria

- [ ] Можно описать сцену ассетом.
- [ ] Можно создавать prefab entities.
- [ ] Tilemap пригоден для production 2D.
- [ ] Audio закрывает базовые нужды игры.
- [ ] Есть samples для scene/prefab/tilemap/audio.

## 0.8 Gameplay Runtime

Цель: закрыть базовые runtime-системы, нужные большинству 2D игр.

### UI Minimal

В 1.0 делать не полноценный UI framework, а минимальную runtime UI систему, достаточную для меню и HUD sample game.

- [ ] Поддержать:
  - [ ] screen-space UI;
  - [ ] basic layout: vertical, horizontal, anchor, padding/margin;
  - [ ] buttons;
  - [ ] labels;
  - [ ] images;
  - [ ] panels;
  - [ ] input fields basic;
  - [ ] focus;
  - [ ] pointer events;
  - [ ] keyboard navigation basic;
  - [ ] text input integration;
  - [ ] UI hot reload where practical.
- [ ] Не делать в 1.0:
  - [ ] complex markup language;
  - [ ] full data binding;
  - [ ] full styling system;
  - [ ] world-space UI;
  - [ ] complex animations;
  - [ ] virtualized lists;
  - [ ] full localization framework.
- [ ] ImGui остается debug/tools overlay only.

### Localization Lite

- [ ] Add string table assets.
- [ ] Add lookup API usable by UI/text.
- [ ] Add missing-key diagnostics.
- [ ] Full pluralization/font fallback/runtime language switching stays post-1.0.

### Animation

- [ ] General 2D animation system:
  - [ ] sprite sheets/atlases;
  - [ ] frame clips;
  - [ ] playback state;
  - [ ] speed;
  - [ ] loop;
  - [ ] animation events;
  - [ ] animation assets;
  - [ ] ECS integration;
  - [ ] simple state machine.
- [ ] Упростить для 1.0:
  - [ ] без blend trees;
  - [ ] без сложных property tracks;
  - [ ] без полноценного visual graph.

### Tween

- [ ] Stabilize:
  - [ ] transform tween;
  - [ ] color tween;
  - [ ] UI tween;
  - [ ] easing functions;
  - [ ] no-GC update after creation;
  - [ ] ECS-friendly handles.

### Shader / Effect Support Basic

- [ ] Shader/effect asset type.
- [ ] Effect parameter blocks.
- [ ] No boxing/reflection in hot paths.
- [ ] Sprite effects.
- [ ] Text effects where practical.
- [ ] Post-processing through render targets.
- [ ] Shader validation in content pipeline.
- [ ] Не делать в 1.0:
  - [ ] complex pipeline variants system;
  - [ ] visual shader graph;
  - [ ] advanced material editor.

### Basic 2D Lighting Optional

- [ ] Максимум для 1.0:
  - [ ] ambient;
  - [ ] simple point lights;
  - [ ] light render target;
  - [ ] debug view.
- [ ] Перенести post-1.0:
  - [ ] shadows;
  - [ ] occluders;
  - [ ] normal maps for tilemaps;
  - [ ] complex area lights;
  - [ ] full 2D lighting pipeline.

### Done Criteria

- [ ] Есть минимальная UI для меню/HUD.
- [ ] Есть sprite animation.
- [ ] Есть tween.
- [ ] Есть basic shader/effect support.
- [ ] Lighting либо minimal, либо отключена из scope.
- [ ] Есть samples для UI/animation/tween/effects.

## 0.9 Networking / Replay / Diagnostics

Цель: сделать базовую сетевую и deterministic foundation, не обещая полноценный production rollback framework.

### Networking Base

- [ ] На базе LiteNetLib:
  - [ ] client/server bootstrap;
  - [ ] RPC;
  - [ ] message protocol;
  - [ ] typed serialization;
  - [ ] connection lifecycle;
  - [ ] disconnect/reconnect basics;
  - [ ] packet diagnostics.

### Action Networking Sample

- [ ] Сделать sample, а не обещать full framework:
  - [ ] input history;
  - [ ] authoritative snapshots;
  - [ ] basic client prediction;
  - [ ] basic server reconciliation;
  - [ ] rollback sample;
  - [ ] bounded ring buffers without runtime allocations;
  - [ ] deterministic shared simulation rules documented.

### ECS Snapshots

- [ ] Foundation:
  - [ ] ECS state snapshot;
  - [ ] delta where practical;
  - [ ] serialization-safe components;
  - [ ] snapshot size diagnostics;
  - [ ] state hash for debugging;
  - [ ] allocation tests.

### Replay / Determinism

- [ ] Input recording.
- [ ] Playback.
- [ ] Deterministic verification.
- [ ] ECS state hash per N ticks.
- [ ] Desync detection diagnostics.
- [ ] Determinism test harness for fixed-tick replay.

### Network Diagnostics

- [ ] RTT.
- [ ] Packet loss.
- [ ] Snapshot size.
- [ ] Prediction error.
- [ ] Rollback count.
- [ ] Resend/queue stats.
- [ ] Debug overlay.

### Headless Server

- [ ] Add headless server template/sample.
- [ ] Add basic packaging path for dedicated server.
- [ ] Add server-side diagnostics hooks.

### Done Criteria

- [ ] Есть networked 2D sample.
- [ ] Есть replay sample.
- [ ] Есть snapshot/hash diagnostics.
- [ ] Rollback показан как рабочий sample.
- [ ] Production limitations documented.
- [ ] Есть headless server sample.

## 1.0 Stabilization / Release

Цель: сделать релиз, который можно использовать без ручной сборки и постоянного чтения исходников движка.

### Build

- [ ] Full solution builds without manual steps.
- [ ] Clean Debug/Release configurations.
- [ ] Deterministic build where practical.
- [ ] CI pipeline.
- [ ] Package artifacts.
- [ ] Versioning.
- [ ] Changelog.

### CLI

- [ ] Добавить `karpik` CLI:
  - [ ] create project;
  - [ ] build assets;
  - [ ] validate project;
  - [ ] run project;
  - [ ] package project;
  - [ ] print module graph;
  - [ ] print asset graph;
  - [ ] run tests/smoke where practical.

### Packaging / Deployment Minimum

- [ ] Windows Debug/Release packaging.
- [ ] Native dependencies check.
- [ ] Headless server packaging.
- [ ] Clear runtime error if platform dependencies are missing.
- [ ] Linux/macOS/web/mobile stay post-1.0 unless already cheap.

### Templates

- [ ] `2D Game`.
- [ ] `Networked 2D Game`.
- [ ] `Tool/Editor`.
- [ ] Minimal sample module.
- [ ] Custom renderer sample where practical.

### Samples

- [ ] Renderer/camera/text.
- [ ] Content pipeline.
- [ ] Input.
- [ ] Input actions.
- [ ] Audio.
- [ ] UI.
- [ ] Tilemap.
- [ ] Animation.
- [ ] Tween.
- [ ] Effects.
- [ ] Networking prediction/rollback sample.
- [ ] Replay/desync sample.
- [ ] Module system sample.
- [ ] Save/config sample.
- [ ] Headless server sample.

### Debug Tooling

Через ImGui/debug overlay:

- [ ] ECS entity inspector.
- [ ] Component viewer.
- [ ] System execution timeline.
- [ ] Scheduler conflict view.
- [ ] System execution-order report / visualization.
- [ ] Job stats.
- [ ] Renderer stats.
- [ ] Asset hot reload log.
- [ ] Input state viewer.
- [ ] Audio voice viewer.
- [ ] Network stats.
- [ ] Replay/desync diagnostics.
- [ ] Debug draw toggles.

### Profiling

- [ ] CPU time per system.
- [ ] CPU time per phase.
- [ ] Job timings.
- [ ] Render submission time.
- [ ] Draw call/batch counters.
- [ ] Asset lookup counters.
- [ ] Allocation counters in debug builds.
- [ ] Optional export to JSON/CSV.

### Logging / Crash Diagnostics

- [ ] Structured logs.
- [ ] Per-run log folder.
- [ ] Fatal error reporting path.
- [ ] Crash/error summary file where practical.
- [ ] Clear diagnostics for missing assets/modules/native dependencies.

### Allocation Tests

- [ ] Renderer submission.
- [ ] ECS scheduler.
- [ ] Input.
- [ ] Input action mapping.
- [ ] Networking snapshots.
- [ ] Jobs.
- [ ] Asset lookup.
- [ ] Audio play/update.
- [ ] Tilemap rendering.
- [ ] UI update for steady-state screens.

### Benchmarks

- [ ] Batching.
- [ ] Scheduler.
- [ ] Jobs.
- [ ] Rollback snapshots.
- [ ] Tilemap rendering.
- [ ] Input update.
- [ ] Asset dependency lookup.
- [ ] Audio voice update.
- [ ] Scene/prefab instantiate.

### Integration / Smoke Tests

- [ ] Client starts and renders scene.
- [ ] Input works.
- [ ] Audio plays.
- [ ] Content pipeline builds and loads assets.
- [ ] Asset hot reload updates running client.
- [ ] UI receives focus/text input.
- [ ] Tilemap renders.
- [ ] Animation plays.
- [ ] Networked sample runs.
- [ ] Replay sample runs.
- [ ] Save/config roundtrip works.

### Docs

- [ ] Quickstart.
- [ ] Project structure.
- [ ] Module system.
- [ ] ECS state rules.
- [ ] System lifecycle.
- [ ] Scheduling.
- [ ] Jobs.
- [ ] Memory/allocators.
- [ ] Renderer.
- [ ] Camera.
- [ ] Text rendering.
- [ ] Input.
- [ ] Input actions.
- [ ] Content pipeline.
- [ ] Scenes.
- [ ] Prefabs.
- [ ] Tilemaps.
- [ ] Audio.
- [ ] UI.
- [ ] Animation.
- [ ] Networking.
- [ ] Replay/determinism.
- [ ] Save/config.
- [ ] VFS/user data paths.
- [ ] Debugging/profiling.
- [ ] Client/Server/Shared architecture.
- [ ] MonoGame concept -> KarpikEngine equivalent.
- [ ] Release checklist.

### Release Criteria

KarpikEngine 1.0 считается готовым, если:

- [ ] Можно создать проект из template.
- [ ] Можно собрать assets через CLI.
- [ ] Можно запустить sample game.
- [ ] Renderer/input/audio/tilemap/UI/animation работают без ручных шагов.
- [ ] Steady-state hot paths проходят allocation tests.
- [ ] Есть базовый networked sample.
- [ ] Есть replay/desync diagnostics.
- [ ] Есть save/config foundation.
- [ ] Есть Windows packaging и headless server packaging.
- [ ] Docs достаточно для первого проекта.
- [ ] Нет критичных архитектурных TODO в runtime foundation.

## MonoGame Coverage Matrix

| MonoGame Area | KarpikEngine 1.0 Equivalent |
| --- | --- |
| Game lifecycle / `GameTime` | System lifecycle, `Time`, fixed/update/render phases, module bootstrap |
| `GameComponent` | ECS systems, modules, DI, lifecycle interfaces |
| `GraphicsDevice` basics | Backend abstraction, render targets, viewport/scissor, states, diagnostics |
| `SpriteBatch` | `IRenderer`, command-buffer renderer, batching diagnostics |
| `SpriteFont` | Atlas-backed SDF/MSDF fonts |
| ContentManager / MGCB | Asset manifest, processors, typed handles, dependency graph, CLI tooling |
| Keyboard / Mouse / Text | No-GC input snapshots, separated text input |
| GamePad / Touch | Gamepad state, optional touch abstraction |
| SoundEffect / Song | `Audio.Core`, sound instances, streamed music, mixer/buses |
| Effects / Shaders | Shader/effect assets, parameter blocks, render-target based post-processing |
| RenderTarget2D | Render targets, post-processing |
| Tools/Templates | CLI, templates, samples, quickstart, troubleshooting |
| 3D | Not planned for 1.0 |
| MonoGame API compatibility | Not planned for core; possible post-1.0 compatibility layer |

