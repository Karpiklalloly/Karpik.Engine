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

- [ ] Выводить side, plugin id, logical module id и implementation из структуры каталогов и имен `.csproj`.
- [ ] Хранить enabled/disabled и выбранную implementation в структурированных `KarpikModuleSelection`.
- [ ] Использовать `KarpikModuleDependency` как единственный source-level project dependency item в `Modules` и `MyGame`.
- [ ] Статически преобразовывать `KarpikModuleDependency` в MSBuild `ProjectReference`, чтобы IDE сразу видела типы.
- [ ] Поддержать required/optional runtime dependencies.
- [ ] Добавить configuration schema для build-time validation без runtime binding.

### Module Validation

- [ ] Валидировать module graph:
  - [ ] missing dependencies;
  - [ ] required/disabled conflicts;
  - [ ] circular dependencies.
- [ ] Валидировать conventions и запрещать прямой source-level `ProjectReference` в `Modules` и `MyGame`.
- [ ] Строить deterministic topo-order загрузки DLL для Client и Server.
- [ ] Оставить `[Module(priority)]` для installer lifecycle, добавить deterministic tie-break и reverse destroy order.
- [ ] Выдавать clear errors before runtime.
- [ ] Хранить generated manifest внутри `Generated/ModuleLoader.cs`.
- [ ] Покрыть module graph тестами.

Подробный план: [`plans/module-graph-execplan.md`](../../plans/module-graph-execplan.md).

## 0.5 Scheduler / Jobs / Memory

Цель: сделать безопасную основу для parallel ECS systems и no-GC job execution.

### ECS Access Metadata

Для 1.0 нужна практичная система, не "магический идеальный analyzer".

- [ ] Поддержать явные атрибуты:
  - [ ] `[Reads<T>]`
  - [ ] `[Writes<T>]`
  - [ ] `[Reads(typeof(T))]`
  - [ ] `[Writes(typeof(T))]`
  - [ ] `[RunsAfter<TSystem>]`
  - [ ] `[RunsBefore<TSystem>]`
- [ ] Analyzer-lite должен проверять простые случаи:
  - [ ] system получает `EcsPool<T>` -> write;
  - [ ] system получает `EcsReadonlyPool<T>` -> read;
  - [ ] aspect fields;
  - [ ] known helper wrappers.
- [ ] Analyzer не обязан идеально понимать для 1.0:
  - [ ] сложный control flow;
  - [ ] reflection;
  - [ ] indirect calls;
  - [ ] runtime-generated access;
  - [ ] aliasing через generic abstractions.
- [ ] Добавить conservative fallback:
  - [ ] unresolved access = conflict;
  - [ ] explicit override attributes;
  - [ ] diagnostics;
  - [ ] ability to disable parallelization for system.
- [ ] Control-flow-aware analysis оставить как stretch внутри 0.5 или перенести post-1.0, если начнет блокировать релиз.

### Scheduler

- [ ] Интегрировать изолированный прототип `IEcsRunParallel` в scheduler backend для `ISystemUpdate`.
- [ ] Определить client runtime threading model:
  - [ ] вызывать `MainThreadScheduler.Execute()` на OS/main thread;
  - [ ] выполнять simulation phases на выделенном simulation thread;
  - [ ] оставить platform/input часть `Begin` и submit/present часть `Render` на main thread;
  - [ ] передавать immutable input snapshots и double-buffered render commands через границу потоков;
  - [ ] добавить явные barriers для shutdown и hot reload.
- [ ] Добавить explicit thread-affinity metadata:
  - [ ] main-thread-only escape hatch для scheduled `ISystemUpdate` / `ISystemFixedUpdate`;
  - [ ] scheduler-owned dispatch через `MainThreadScheduler`;
  - [ ] диагностику попыток выполнить main-thread-only system на worker thread;
  - [ ] тесты, доказывающие, что main-thread-only systems не попадают в worker queue.
- [ ] Использовать generated/manual read-write metadata.
- [ ] Строить safe parallel groups.
- [ ] Валидировать read/write conflicts.
- [ ] Учитывать `RunAfter` / `RunBefore`.
- [ ] Поддержать cycle detection для explicit ordering.
- [ ] Добавить diagnostics for invalid order.
- [ ] Добавить visualization/debug dump of execution order.
- [ ] Поддержать fixed phase groups.
- [ ] Поддержать update phase groups.
- [ ] Поддержать render submit phase groups.
- [ ] Добавить single-thread fallback mode.
- [ ] Добавить deterministic scheduling option.

### Karpik.Jobs

- [ ] Стабилизировать как отдельный submodule/product:
  - [ ] public API;
  - [ ] standalone tests;
  - [ ] benchmarks;
  - [ ] docs;
  - [ ] standalone usage examples.
- [ ] Обязательные features к 1.0:
  - [ ] job handles;
  - [ ] dependencies;
  - [ ] worker pool;
  - [ ] clean shutdown;
  - [ ] no-GC scheduling after warm-up;
  - [ ] simple work stealing or equivalent balancing;
  - [ ] optimized cancellation path;
  - [ ] exception handling/reporting;
  - [ ] profiler hooks.
- [ ] Необязательно к 1.0:
  - [ ] сложные scheduling heuristics;
  - [ ] fiber-like execution;
  - [ ] custom task graph editor.

### Memory

- [ ] Добавить unmanaged allocators:
  - [ ] arena allocator;
  - [ ] linear allocator;
  - [ ] pool allocator;
  - [ ] explicit lifetime/dispose;
  - [ ] `Span<T>` / ref-friendly wrappers;
  - [ ] leak diagnostics;
  - [ ] double-dispose checks where practical;
  - [ ] debug mode validation.

### Allocation Tests

- [ ] Job scheduling.
- [ ] ECS scheduler.
- [ ] System metadata lookup.
- [ ] Fixed update frame.
- [ ] Render submit path.
- [ ] Common allocator usage.

### Done Criteria

- [ ] Есть safe parallel ECS execution.
- [ ] Можно отключить parallel execution.
- [ ] No-GC scheduling после warm-up.
- [ ] Есть diagnostics конфликтов.
- [ ] Jobs можно использовать отдельно.
- [ ] Есть базовые benchmarks.

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

### Camera2D

- [ ] Position.
- [ ] Zoom.
- [ ] Rotation.
- [ ] Viewport.
- [ ] Screen-to-world.
- [ ] World-to-screen.
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

- [ ] Перевести input module на no-GC snapshot API.
- [ ] Поддержать:
  - [ ] keyboard held/pressed/released;
  - [ ] mouse position;
  - [ ] mouse delta;
  - [ ] mouse wheel;
  - [ ] mouse buttons;
  - [ ] text input separately from key input;
  - [ ] gamepad buttons;
  - [ ] gamepad sticks;
  - [ ] gamepad triggers;
  - [ ] gamepad connection/disconnection;
  - [ ] optional touch abstraction;
  - [ ] input capture integration for UI/debug overlay.

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

