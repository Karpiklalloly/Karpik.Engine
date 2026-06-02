# KarpikEngine Post-1.0 Roadmap

Post-1.0 цель - развивать KarpikEngine из сильного 2D runtime/framework в полноценный 2D engine ecosystem.

Главные направления:

- editor;
- advanced tooling;
- production networking;
- advanced rendering;
- modding;
- AI;
- deployment;
- ecosystem;
- optional MonoGame/FNA compatibility layer.

## 1.1 Editor Foundation

Цель: сделать базовый редактор, который ускоряет разработку игр, но не пытается сразу конкурировать с Unity/Godot.

### Editor App

- [ ] Отдельное editor приложение.
- [ ] Project browser.
- [ ] Scene view.
- [ ] Game view.
- [ ] Dockable panels.
- [ ] Asset browser.
- [ ] Console/log panel.
- [ ] Inspector.
- [ ] Hierarchy/entities panel.
- [ ] Play/edit mode separation.

### ECS Inspector

- [ ] Entity list.
- [ ] Component viewer.
- [ ] Component editor.
- [ ] Add/remove components.
- [ ] Search/filter.
- [ ] Component diff.
- [ ] Runtime state view.
- [ ] Prefab instance view.

### Scene Editing

- [ ] Create/delete entities.
- [ ] Transform gizmos.
- [ ] Camera controls.
- [ ] Selection.
- [ ] Snap/grid.
- [ ] Scene save/load.
- [ ] Prefab placement.
- [ ] Tilemap placement basic.

### Asset Tooling

- [ ] Asset browser.
- [ ] Dependency graph view.
- [ ] Reimport.
- [ ] Hot reload status.
- [ ] Broken references view.
- [ ] Asset validation panel.

### Runtime Debugging

- [ ] Attach editor to running game.
- [ ] Inspect ECS world.
- [ ] Inspect renderer stats.
- [ ] Inspect input state.
- [ ] Inspect audio voices.
- [ ] Inspect network stats.

## 1.2 Advanced Asset Pipeline

Цель: сделать content workflow надежным для средних и больших проектов.

### Asset Database

- [ ] Stable GUIDs.
- [ ] Asset metadata.
- [ ] Import settings.
- [ ] Dependency tracking.
- [ ] Incremental builds.
- [ ] Cache invalidation.
- [ ] Asset bundles/packs.
- [ ] Platform-specific processing.

### Importers

- [ ] Texture importer settings.
- [ ] Font importer settings.
- [ ] Audio importer settings.
- [ ] Shader importer settings.
- [ ] Tilemap importer settings.
- [ ] Animation importer settings.

### Asset Variants

- [ ] Platform variants.
- [ ] Quality variants.
- [ ] Localization variants.
- [ ] DLC/mod variants.

### Build Pipeline

- [ ] Build profiles.
- [ ] Development/release assets.
- [ ] Compression settings.
- [ ] Bundle versioning.
- [ ] Patch generation where practical.

## 1.3 Advanced UI

Цель: превратить minimal UI из 1.0 в полноценную runtime UI систему.

### Layout

- [ ] Flexible layout.
- [ ] Grid layout.
- [ ] Scroll containers.
- [ ] Virtualized lists.
- [ ] Responsive anchors.
- [ ] Safe area support.
- [ ] World-space UI.

### Styling

- [ ] Style assets.
- [ ] Themes.
- [ ] Pseudo-states.
- [ ] Transitions.
- [ ] Font/style inheritance.

### Markup

- [ ] Optional markup assets.
- [ ] Hot reload.
- [ ] Validation.
- [ ] Editor preview.

### Binding

- [ ] Explicit data binding.
- [ ] No-reflection hot path.
- [ ] Generated bindings where practical.
- [ ] ECS-friendly binding model.

### Localization

- [ ] Text tables.
- [ ] Pluralization.
- [ ] Font fallback.
- [ ] Runtime language switching.

## 1.4 Advanced 2D Rendering

Цель: сделать 2D renderer конкурентным для визуально богатых игр.

### 2D Lighting

- [ ] Point lights.
- [ ] Area lights.
- [ ] Ambient.
- [ ] Light masks.
- [ ] Normal maps for sprites.
- [ ] Normal maps for tilemaps.
- [ ] Shadows.
- [ ] Occluders.
- [ ] Debug views.
- [ ] Batching-aware lighting.

### Materials

- [ ] Sprite materials.
- [ ] Text materials.
- [ ] Tilemap materials.
- [ ] Material parameter blocks.
- [ ] Material variants.

### Effects

- [ ] Post-processing stack.
- [ ] Bloom.
- [ ] Blur.
- [ ] Color grading.
- [ ] Screen distortion.
- [ ] CRT/pixel effects.
- [ ] Palette effects.
- [ ] Custom passes.

### Render Graph

- [ ] 2D render graph.
- [ ] Pass dependencies.
- [ ] Render target lifetime.
- [ ] Debug visualization.

### Advanced Batching

- [ ] Texture arrays/atlases.
- [ ] Bindless-like abstraction where supported.
- [ ] Sorting strategies.
- [ ] Batching profiler.

## 1.5 Production Networking

Цель: превратить networking sample/foundation из 1.0 в production-ready multiplayer framework.

### Prediction / Rollback

- [ ] Robust client prediction.
- [ ] Server reconciliation.
- [ ] Rollback framework.
- [ ] Input delay support.
- [ ] Interpolation/extrapolation.
- [ ] Rollback-safe events.
- [ ] Rollback-safe audio/visual effects.
- [ ] Deterministic tick management.

### Snapshots

- [ ] ECS snapshot delta compression.
- [ ] Component-level serialization.
- [ ] Interest management.
- [ ] Entity spawn/despawn replication.
- [ ] Bandwidth budgeting.
- [ ] Snapshot prioritization.

### Diagnostics

- [ ] Desync debugger.
- [ ] State hash timeline.
- [ ] Rollback visualizer.
- [ ] Packet capture/replay.
- [ ] Simulated network conditions: latency, jitter, loss, duplication, reordering.

### Server

- [ ] Headless server template.
- [ ] Server profiling.
- [ ] Server metrics.
- [ ] Graceful shutdown.
- [ ] Admin commands.
- [ ] Matchmaking hooks where practical.

## 1.6 Modding Platform

Цель: сделать управляемый и безопасный modding API.

### Unified Mod API

- [ ] Attributed C# API registry.
- [ ] Expose approved APIs to Lua/JS.
- [ ] Side policy: Client, Server, Shared.
- [ ] Permissions.
- [ ] Capability checks.
- [ ] Versioning.
- [ ] Diagnostics for exposed APIs.
- [ ] Serialization-safe exposed types.

### Sandbox

- [ ] Restricted API surface.
- [ ] File access policy.
- [ ] Network access policy.
- [ ] CPU/time budget where practical.
- [ ] Memory budget where practical.

### Mod Packaging

- [ ] Mod manifest.
- [ ] Dependencies.
- [ ] Version constraints.
- [ ] Load order.
- [ ] Conflicts.
- [ ] Enable/disable mods.

### Mod Tooling

- [ ] Validation CLI.
- [ ] Mod template.
- [ ] Hot reload.
- [ ] Debug logs.
- [ ] API docs generation.

## 1.7 AI Runtime

Цель: добавить game AI tools как runtime + authoring assets.

### Behavior Trees

- [ ] BT runtime.
- [ ] BT definitions as assets.
- [ ] ECS components/systems for ticking.
- [ ] Blackboard.
- [ ] Decorators.
- [ ] Services.
- [ ] Hot reload.
- [ ] Debug visualization.

### Utility AI

- [ ] Utility scorers.
- [ ] Considerations.
- [ ] Curves.
- [ ] ECS integration.
- [ ] Asset definitions.
- [ ] Debug visualization.

### Navigation

- [ ] 2D grid navigation.
- [ ] Tilemap navigation.
- [ ] Flow fields where practical.
- [ ] Local avoidance basic.

### Editor Support

- [ ] BT editor.
- [ ] Utility AI editor.
- [ ] Runtime debugger.

## 1.8 Animation 2.0

Цель: расширить animation system для более сложных игр.

### Features

- [ ] Animation state machines advanced.
- [ ] Blend trees.
- [ ] Layered animation.
- [ ] Animation graph assets.
- [ ] Property tracks.
- [ ] Event tracks.
- [ ] Timeline/cutscene basics.
- [ ] Root motion optional.
- [ ] Editor preview.

### 2D Character Animation

- [ ] Skeletal 2D integration where practical.
- [ ] Sprite swapping.
- [ ] Hitbox animation.
- [ ] Hurtbox animation.
- [ ] Animation-driven events.

## 1.9 Physics 2D Advanced

Цель: усилить Physics2D для production usage.

- [ ] Advanced collision filtering.
- [ ] Sensors/triggers.
- [ ] Continuous collision detection where practical.
- [ ] Physics debug draw.
- [ ] Joints.
- [ ] Character controller helpers.
- [ ] Deterministic physics mode investigation.
- [ ] Tilemap collision optimization.
- [ ] Full-double physics if 1.0 keeps a float backend boundary.

## 2.0 Editor + Ecosystem Release

Цель: собрать post-1.0 фичи в большой релиз с полноценным workflow.

### Editor Maturity

- [ ] Stable editor.
- [ ] Scene editing.
- [ ] Prefab editing.
- [ ] Tilemap editing.
- [ ] UI editing.
- [ ] Animation editing.
- [ ] Asset import settings.
- [ ] Profiling panels.
- [ ] Debugging panels.

### Deployment

- [ ] Windows packaging.
- [ ] Linux packaging.
- [ ] macOS packaging where practical.
- [ ] Web export investigation.
- [ ] Android/iOS investigation.
- [ ] Dedicated server packaging.

### Marketplace / Ecosystem Optional

- [ ] Package registry.
- [ ] Template registry.
- [ ] Community samples.
- [ ] Plugin system.
- [ ] Mod browser where practical.

## Optional: MonoGame/FNA Compatibility Layer

Цель: упростить миграцию с MonoGame/FNA, не загрязняя Karpik-native core.

### Scope

- [ ] Optional package.
- [ ] `SpriteBatch`-like wrapper.
- [ ] `GameTime` adapter.
- [ ] Basic content adapter.
- [ ] Input adapter.
- [ ] Texture/font/audio mapping.

### Не делать

- [ ] Полную API совместимость.
- [ ] 3D compatibility.
- [ ] XNA behavior quirks в core.

## Приоритеты Post-1.0

### Высокий Приоритет

1. Editor foundation.
2. Advanced asset pipeline.
3. Production networking.
4. Advanced diagnostics/profiling.
5. Advanced UI.

### Средний Приоритет

1. Advanced 2D rendering.
2. Modding.
3. Animation 2.0.
4. Physics advanced.

### Низкий / Опциональный Приоритет

1. AI tools.
2. MonoGame compatibility layer.
3. Web/mobile export.
4. Marketplace/package ecosystem.

## Главный Принцип Post-1.0

Не ломать базовые правила KarpikEngine:

- no-GC hot paths;
- ECS/data-oriented runtime;
- hot reload friendliness;
- Client/Server/Shared boundaries;
- fixed simulation support;
- renderer command-buffer model;
- explicit state ownership;
- tooling без скрытой магии в runtime.

