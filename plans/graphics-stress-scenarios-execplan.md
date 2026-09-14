# Add self-contained graphics stress scenarios

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Extend the ImGui graphics load probe beyond identical rectangles. Developers can select reproducible scenarios that separately exercise ordered batching, out-of-order command sorting, normal texture batching, and worst-case texture switching. The Copy timings output identifies the active scenario.

## Progress

- [x] (2026-07-12) Initial plan created and scenario design approved by the developer.
- [x] (2026-07-12) Add scenario selection and deterministic command generation.
- [x] (2026-07-12) Add owned synthetic texture resources and texture stress scenarios.
- [x] (2026-07-12) Run automated tests and build; manual client validation remains.

## Surprises & Discoveries

- Observation: `ThreadBuffer` uses insertion sort only when a submitted command has a lower sort key than its predecessor.
  Evidence: `Modules/Client/Graphics/Graphics.Core/Buffers/ThreadBuffer.cs`, `SortCommandsIfNeeded`.
- Observation: the existing rectangle scenario has monotonic equal sort keys, so it does not exercise sorting or resource-set changes.
  Evidence: captured merge timings show `Merge sort: 0.00 ms`; `GraphicsLoadTestRenderPrepareSystem` emits only `DrawRectCmd`.

## Decision Log

- Decision: use synthetic resources, not game assets.
  Rationale: engine benchmarks must be deterministic and independent of project content and hot reload asset state.
  Date/Author: 2026-07-12 / developer and AI assistant
- Decision: create a small fixed set of 1x1 GPU textures once during graphics initialization; no texture or managed allocation is permitted in `RenderPrepare`.
  Rationale: isolate command ordering and batching cost from asset IO and per-frame allocation.
  Date/Author: 2026-07-12 / AI assistant

## Outcomes & Retrospective

The four scenario controls, synthetic resources, and timing export are implemented. `Graphics.Core.Tests` passed 38/38, `Karpik.Engine.Core.Runner.Tests` passed 27/27, and `ClientLauncher` built with zero errors. Runtime validation remains necessary because OpenGL texture/resource-set creation requires a live graphics context.

## Context and Orientation

`GraphicsLoadTestSettings` is registered by `GraphicsCoreInstaller` and controlled through the ImGui panel in `ECS/ImGuiSystems.cs`. `GraphicsLoadTestRenderPrepareSystem` emits the synthetic commands during RenderPrepare. `MergeThread` reads the command buffers, conditionally sorts them through `ThreadBuffer.GetCommands()`, and records batches into a Veldrid command list.

The active scenarios are:

- Sorted rectangles: baseline for vertex generation and one stable resource set.
- Unsorted rectangles: deterministic descending/interleaved sort keys to execute `ThreadBuffer` insertion sort.
- Texture batches: commands grouped by a small fixed texture set to model conventional spritesheet/material batching.
- Texture thrash: texture changes every command to measure the intentional worst case for resource-set and draw-call encoding.

## Real-Time Assessment

This work changes the RenderPrepare hot path. Per-frame work is a dense counted loop over the selected quad count. It must not use LINQ, closures, collections, asset loading, or managed allocation. Synthetic textures and their `ITexture2D` wrappers are created once and disposed with the graphics module. The change is client-only. It adds no locks, waits, or shared mutable state beyond the existing volatile settings fields.

## Plan of Work

1. Add an enum-backed scenario setting and display it in ImGui and copied timing text.
2. Add a small disposable synthetic texture provider in Graphics.Core that owns a fixed number of 1x1 Veldrid textures/resource sets.
3. Update RenderPrepare to select the command pattern. Keep the existing rectangle baseline. Use existing `AddTexture` commands for texture cases and deterministic sort keys for the unsorted case.
4. Test scenario clamping/formatting and resource-independent command pattern helpers. Build the affected client and run the core tests.
5. Manually run each scenario at 1k/4k/16k and inspect merge-sort, merge-buffer-update, merge-draw-encode, allocations, and GPU timings.

## Milestones

1. Settings/UI/clipboard identify the active scenario. Validate Graphics.Core tests.
2. Synthetic texture ownership and command generation compile with no hot-path allocation. Validate ClientLauncher build and Core tests.
3. Manual client run exposes all four selectable scenarios and reports zero merge allocations after warm-up.

## Concrete Steps

From `C:\Users\artem\RiderProjects\KarpikEngine`:

1. `dotnet test Modules\Client\Graphics\Graphics.Core.Tests\Graphics.Core.Tests.csproj -m:1 -nr:false --no-restore`
2. `dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --no-restore`
3. `dotnet build ClientLauncher\ClientLauncher.csproj -m:1 -nr:false --no-restore`

## Validation and Acceptance

The ImGui panel must select each named scenario and clipboard text must include that name. The ordered rectangle baseline must remain available. The unsorted scenario must produce a nonzero merge-sort time at meaningful command counts. Texture thrash must produce materially more draw encoding/buffer work than texture batches. After the warm-up frame, Merge allocations must remain zero.

## Idempotence and Recovery

Scenario resources are created and disposed through the graphics module lifetime. Rebuilding or hot reloading recreates them through normal module setup. If a driver rejects synthetic texture creation, the provider should fail setup explicitly and the change can be reverted as one isolated Graphics.Core feature.

## Artifacts and Notes

The baseline timings captured on 2026-07-12 show vertex construction as the dominant merge cost at 16k rectangles; this plan tests whether sorting and resource switches introduce separate bottlenecks.
