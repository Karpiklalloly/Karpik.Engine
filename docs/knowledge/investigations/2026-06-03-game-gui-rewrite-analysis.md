---
title: "Analysis of Habr articles: why and how game GUI is rewritten"
date: "2026-06-03"
updated: "2026-06-05"
tags:
  - knowledge
  - investigation
  - gui
  - gamedev
  - rendering
---

# Analysis of Habr Articles: Why and How Game GUI Is Rewritten

## Question

What practical architecture lessons should KarpikEngine take from Sergei Kushnirenko's Habr articles about why and how game GUI systems are rewritten?

Sources:

- Part 1, "Почему игровой GUI пишут заново (Ч.1)": https://habr.com/ru/articles/1039592/
- Part 2, "Как игровой GUI пишут заново (Ч.2)": https://habr.com/ru/articles/1041522/

## Context

The first article surveys why game teams repeatedly rewrite GUI systems. It describes the long-lived retained-mode GUI skeleton used across engines: control trees, reflected properties, property bindings, templates/prefabs, input dispatch, layout constraints, 9-slice rendering, integer pixel alignment, engine abstraction through Bridge-style backends, localization, text rendering, effects, render-to-texture, and diegetic UI.

The second article classifies GUI architecture along independent axes: where UI lives relative to the game world, what function it serves, how it is stored in memory, how data flows into it, how elements are laid out, and how it is rendered. Its practical point is that there is no single correct UI architecture; HUD, menus, prompts, tools, and diegetic screens have different update rates, latency needs, authoring needs, and rendering constraints.

This note evaluates the article through KarpikEngine constraints: real-time suitability, zero avoidable allocations in hot paths, predictable frame cost, clean Client/Shared boundaries, and data-oriented implementation where many UI elements are processed every frame.

## Findings

- The core reason game GUI gets rewritten is not the lack of a tree widget model. It is the accumulation of incompatible requirements: designer-editable layout, pixel-perfect rendering, localization, input routing, animation/effects, multiple platforms, render backend independence, game-code binding, and in-world UI. A "simple UI layer" becomes a subsystem.

- The retained control tree is still a useful authoring and ownership model, but it must not become the runtime hot path by default. Naive repeated tree walks for layout, hit testing, visibility, effects, and rendering create pointer chasing and unpredictable frame cost. Runtime should cache derived data and rebuild it only from explicit dirty flags.

- Reflection/property systems are valuable for editors, serialization, animation, and debugging, but an engine implementation should separate authoring metadata from runtime storage. A runtime path based on string lookup, boxed variants, managed references, or per-control dictionaries would be unsuitable for KarpikEngine hot paths.

- Binding is the right boundary between gameplay and UI internals. Game code should publish stable semantic values such as `hp_value`, `armor_value`, or `selected_unit_name`; it should not search for concrete labels or progress bars. This protects gameplay code from layout edits and lets UI assets change without recompiling game logic.

- Template/prefab-style UI assets are useful, but the runtime should instantiate into compact data structures. Keeping template override resolution, string paths, and diff traversal in frame code would be a predictable source of GC pressure and cache misses.

- Input dispatch should not rely on broad event bubbling as the primary model. Direct dispatch by window/id is better for semantic UI messages, while pointer/touch input needs a cached hit-test structure. Rebuilding hit-test caches every frame because a setter invalidates unchanged values is a real failure mode.

- Non-rectangular hit regions matter for game UI: radial menus, hex maps, circular icons, stylized buttons, and world-projected UI cannot be handled cleanly by rectangle-only hit testing. However, alpha hit testing or arbitrary polygons must be designed with cost controls and cacheability.

- Synchronous UI messages are easier to debug and avoid end-of-frame queue spikes, but they introduce reentrancy risk. If used, handlers need strict rules: bounded work, no unbounded recursion, no allocations in frequent paths, and clear separation between immediate visual state changes and deferred heavy work.

- Layout needs anchors, offsets, min/max constraints, and integer final coordinates. Float layout can be acceptable internally, but final vertex generation for screen UI should snap to integer pixels to avoid blurred text and edges.

- 9-slice and tiled UI geometry should be generated in a way that preserves atlas batching. Shader UV repetition can break atlas usage; repeated quads with atlas UVs keep batching simpler and are often the better game-engine trade-off.

- A reusable GUI layer needs Bridge-style boundaries to renderer, input, audio, scripting/actions, localization, and asset loading. For KarpikEngine this belongs on the Client side, with Shared limited to data contracts that are runtime-side independent.

- Text is a subsystem, not a helper function. The article correctly separates glyph caching, formatted text, localization grammar, protected placeholders, texture localization, and SDF fonts. For KarpikEngine, game code should pass localization keys and domain values, while language-specific grammar should stay isolated in localization data/services.

- Effects should be modeled as transformations between control state and render requests, not as ad hoc fields added to every control. But a virtual `IGuiEffect` chain per control would be expensive in C# if used directly in hot rendering paths. Prefer compiled effect descriptors, dense arrays, or generated render commands.

- Render-to-texture and diegetic UI are not just "2D UI with a transform". They require a separate path for depth, transparency, lighting interaction, update frequency, texture lifetime, and synchronization with scene rendering.

- Immediate-mode UI is useful for tools/debug panels, but it is not a replacement for authored game UI. Dear ImGui-style rebuilding each frame is acceptable for editor/dev tooling if allocations are controlled, but retained authored UI remains the better basis for production HUD/menu systems.

## Additional Findings From Part 2

- UI function should drive architecture. A HUD has few elements but may update every frame; inventory/settings screens may have many elements but update rarely and often while paused; contextual prompts appear/disappear frequently; diegetic screens need scene integration. One subsystem can share primitives, but one runtime policy for all of these is the wrong abstraction.

- The storage model should be chosen by hot-path profile. Scene graphs are readable and good for authoring, but pointer-heavy. Flat lists and SoA arrays are better for runtime iteration, but hierarchy changes require ordering/indirection. Pure ECS UI is tempting in an ECS engine, but UI hierarchy, clipping, z-order, event capture, and parent-before-child layout fight ECS's preferred flat independent-entity model.

- Retained mode is the right baseline for production UI when paired with dirty flags and cached derived data. Immediate mode is still valuable for tools and debug overlays. Retained-immediate hybrids are attractive for developer ergonomics, but they must compile/diff into a no-GC runtime representation rather than rebuild rich objects every frame.

- MVVM/MVC-style patterns are useful only where the engine provides an efficient binder. For game HUD hot paths, a naive binder based on reflection, strings, notifications, or allocations is worse than direct typed binding slots. MVVM is a good fit for menu-heavy screens and tooling, not for continuously changing world state.

- Reactive/observable UI avoids polling, but a large subscription graph becomes hard to debug and can allocate through delegates, closures, iterators, or async/reactive machinery. For KarpikEngine it should not be the default hot-path dataflow.

- Unidirectional data flow is strong for complex menus, shops, inventory, settings, and meta-game UI because actions and state transitions can be logged and replayed. It is a poor fit for per-frame entity/world state if implemented with immutable state copies or per-change allocations.

- Layout should be tiered. Anchors + offsets are the default for production HUD because they are predictable and cheap. Flex/flow layout is useful for lists and menus if recalculated only on dirty input. General constraint solvers belong in editors/offline preprocessing or rare paused screens, not in frame-critical UI.

- Canvas-style batching is useful but dangerous when one changing element forces a whole canvas rebuild. Dynamic and static UI should be separated into independent invalidation/render groups, similar to Unity sub-canvases: static backgrounds should not rebuild because a timer text changed.

- Layer-based rendering should be a first-class concept, not an afterthought. HUD, fullscreen damage/meta effects, menus, tooltips, debug overlays, render-to-texture UI, and VR compositor layers can require different depth, blend, ordering, and render-target rules.

- Pure SVG/vector UI has unpredictable cost because tessellation depends on shape complexity. SDF/MSDF fonts and distance-field vector-like assets are the better game-engine compromise: fixed atlas cost, predictable shader work, and scalable output.

- GPU-driven UI and shader/fullscreen-pass UI are specialized tools. They make sense when CPU command generation is the bottleneck, when there are thousands of icons/markers, or when the UI is really an effect. They should not be the starting point for ordinary HUD/menu work.

## KarpikEngine Implications

- Treat GUI as a Client subsystem with explicit interfaces to rendering, input, assets, audio, actions, and localization. Do not let it depend on Server code.

- Use retained authoring data, but compile it into runtime-friendly structures: stable ids instead of strings, dense arrays instead of object graphs where many controls are processed, dirty flags for layout/hit/render cache invalidation, and preallocated command buffers.

- Keep game-to-UI communication semantic and narrow. Gameplay should write to a view-model-like data contract or UI binding table, not mutate concrete controls by name.

- Do not allow property reflection, localization parsing, template diff resolution, or action lookup to run in per-frame UI rendering.

- Build the rendering side around cached render commands: quads, text runs, clip rectangles, 9-slice geometry, effect descriptors, material/texture handles, and sorted/batched draw data.

- Design input around explicit hit-test caches. A setter should only invalidate when the value actually changes; otherwise a harmless-looking animation or HUD update can force cache rebuilds.

- Prefer integer final screen-space coordinates for 2D UI vertices and text. Keep subpixel animation as authoring/intermediate state, then snap at render command generation.

- Plan localization before UI asset format hardens. Store placeholders as structured tokens, not editable raw markup; keep grammar metadata in localization data, not gameplay code.

- Split screen-space UI and diegetic/render-to-texture UI early. They can share authoring/layout/text code, but their render scheduling and resource ownership differ enough that a single path will become fragile.

- Define UI profiles explicitly:
  - HUD: retained runtime, typed bindings, anchors/manual layout, cached text/quads, no allocations.
  - Menus/inventory/settings: retained authored hierarchy, optional UDF/MVVM-style state layer, flex/flow where useful, dirty-only layout.
  - Contextual prompts: pooled instances, cheap spawn/despawn, stable atlas/material use.
  - Debug/tools: ImGui or immediate-mode wrappers are acceptable.
  - Diegetic UI: separate world/render-to-texture path with explicit depth, transparency, lighting, and update-frequency rules.

- Let ECS feed UI data, but do not make production UI a pure ECS hierarchy. A UI module can read stable snapshots or binding values derived from ECS systems; it should not express every button/label as gameplay ECS entities unless profiling proves that model is better for a narrow case.

- Prefer a layered renderer: static screen UI, dynamic HUD, prompts/tooltips, menus/modals, fullscreen meta effects, debug overlay, and diegetic render targets should be independently invalidated and independently batched.

## Risks / Things To Challenge

- A classic OOP control hierarchy is easy to build but poor for processing many controls under real-time constraints. If KarpikEngine implements this directly, it should be editor/authoring facing, not the final hot runtime layout.

- A general `Variant` property bag is convenient, but in C# it can quietly introduce boxing, heap allocations, string hashing, and type checks. Runtime property storage needs typed slots or generated accessors.

- Per-control virtual effects, event handlers, or delegates can be acceptable for rare interactions, but not for render command generation across hundreds/thousands of elements every frame.

- Async UI event queues are not automatically bad, but unbounded queues processed at frame end are dangerous. If queues are used, they need budgets, coalescing, and instrumentation.

- Importing web/app patterns directly is risky. MVVM, reactive streams, Redux-style state, flexbox, and constraint solvers are useful in selected UI profiles, but none of them should be allowed to allocate or perform unbounded work inside `Update`, `FixedUpdate`, ECS `Run`, or render command generation.

- A generic "UI canvas" abstraction can hide rebuild costs. Any canvas/render group must expose invalidation metrics: number of dirty elements, rebuilt vertices, rebuilt text runs, draw calls, atlas/material breaks, and time spent in layout/hit/render cache rebuild.

## Outcome

The articles are a useful checklist of features and architectural axes that cause game GUI systems to grow beyond the first simple implementation. For KarpikEngine the main lesson is to separate authoring convenience from runtime execution: retained trees, reflected properties, templates, string names, MVVM-like state, reactive updates, and flexible layout are good tools in the right profile, but hot paths should operate on compact cached data, stable ids, explicit dirty flags, typed bindings, layered invalidation, and preallocated render/input structures.

The technically correct direction is not "rewrite GUI until it feels clean" or "pick one universal UI paradigm"; it is to define UI profiles, runtime data layout, dataflow rules, layout cost limits, and invalidation boundaries first, then let editor-facing abstractions compile into those constraints.

## Links

- Source article, part 1: https://habr.com/ru/articles/1039592/
- Source article, part 2: https://habr.com/ru/articles/1041522/
- Related subsystem: Client GUI / rendering / input / localization
- Related code: Client GUI candidate; existing Graphics/Input/Text primitives were not changed by this note
- Related tests: not run; this is a research note
