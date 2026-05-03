# Integrate ImGui Overlay

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Add ImGui to the client renderer as a debug and temporary UI overlay. The first visible outcome is an ImGui window rendered above the existing 2D renderer, toggleable at runtime with `F1`, with text input and mouse interaction working correctly.

The initial integration is not a production gameplay UI system. It is a client-only tool/debug layer that can host temporary panels while the engine UI matures. Gameplay input must respect ImGui capture flags when the overlay is enabled.

## Progress

- [x] (2026-05-03 00:00 +04:00) Initial plan created after inspecting `Graphics.Core`, `Graphics.OpenGL`, `Window.Core`, `Window.Sdl2`, `Input`, and vendored `Veldrid.ImGui`.
- [x] (2026-05-03 21:50 +04:00) Milestone 1 completed: `IInputSource` exposes the raw `InputSnapshot`, `SDL2InputSource` returns the latest snapshot, `Window.Core` registers `InputCaptureState`, and gameplay `Input` suppresses keyboard/text/mouse state when capture flags are active.
- [x] (2026-05-03 22:14 +04:00) Milestone 2 completed: `Graphics.Core` registers `ImGuiOverlayState` and `ImGuiRenderContext`, initializes `ImGuiRenderer` from the main swapchain output, owns a dedicated ImGui command list, forwards resize to ImGui, and clears capture flags while the overlay is disabled.
- [x] (2026-05-03 22:34 +04:00) Milestone 3 completed: final graphics submission is split into scene submit, optional ImGui render submit, and one final swap-buffers system.
- [x] (2026-05-03 22:38 +04:00) Initial usable overlay path added: `F1` toggles the overlay, ImGui receives the raw input snapshot each frame while enabled, capture flags are copied into `InputCaptureState`, and a minimal `Karpik Debug` ImGui window renders frame time, mouse position, and a text input field.
- [x] (2026-05-03 22:53 +04:00) `MyGame.Client.Main` ImGui.NET package version aligned with vendored `Veldrid.ImGui` and the game client project builds with the overlay-access pattern used by `DisplaySystem`.
- [x] (2026-05-03 23:40 +04:00) Final runtime validation completed through `ClientLauncher`: the scene renders, ImGui windows render above it, and an ImGui `InputText` receives typed text when the overlay is enabled.

## Surprises & Discoveries

- Observation: `Veldrid.ImGui` is already vendored under `first-parties/Kveldrid/src/Veldrid.ImGui` and exposes `ImGuiRenderer` over backend-agnostic Veldrid APIs.
  Evidence: `first-parties/Kveldrid/src/Veldrid.ImGui/ImGuiRenderer.cs` accepts `GraphicsDevice`, `CommandList`, `OutputDescription`, and `InputSnapshot`.

- Observation: `Graphics.Core` already references `Veldrid.ImGui`, even though no runtime integration exists yet.
  Evidence: `Modules/Client/Graphics/Graphics.Core/Graphics.Core.csproj` contains a `ProjectReference` to `first-parties/Kveldrid/src/Veldrid.ImGui/Veldrid.ImGui.csproj`.

- Observation: current window input stores the raw Veldrid `InputSnapshot`, but `IInputSource` does not expose it.
  Evidence: `Modules/Client/Window/Window.Sdl2/SDL2InputSource.cs` stores `_snapshot = _window.PumpEvents()`, while `Modules/Client/Window/Window.Core/IInputSource.cs` exposes only derived lists and mouse helpers.

- Observation: current input processing allocates per frame and should not be treated as a no-GC gameplay hot path until separately fixed.
  Evidence: `SDL2InputSource.Update()` uses LINQ, `ToList()`, and `ToImmutableHashSet()` and contains a TODO about per-frame allocations.

- Observation: On this machine, affected project builds can fail with an empty error summary during parallel MSBuild project-reference evaluation.
  Evidence: `dotnet build Modules/Client/Window/Window.Core/Window.Core.csproj` exited with "Ошибок: 0"; the same build succeeded with `--no-restore -p:NoWarn=NU1903 -m:1`.

- Observation: `MyGame.Client.Main` had a direct `ImGui.NET` package reference at `1.91.6.1`, while vendored `Veldrid.ImGui` references `1.90.1.1`.
  Evidence: `rg "ImGui.NET" -n .` showed only those two package references.

## Decision Log

- Decision: Put the first ImGui integration in `Graphics.Core`, not `Graphics.OpenGL`.
  Rationale: `ImGuiRenderer` depends on Veldrid abstractions rather than OpenGL-specific APIs. `Graphics.OpenGL` should only remain responsible for creating and registering the `GraphicsDevice`.
  Date/Author: 2026-05-03 / Codex

- Decision: Treat ImGui as debug and temporary UI for v1, not as the long-term game UI.
  Rationale: ImGui is productive for tools and diagnostics, but it is not designed around the engine's zero-allocation, gameplay-facing UI constraints.
  Date/Author: 2026-05-03 / Codex

- Decision: Use direct ECS ImGui systems for user panels in v1.
  Rationale: The user prefers ECS systems over a panel registry. This keeps the first API small: systems call `ImGuiNET.ImGui` directly between ImGui begin and render.
  Date/Author: 2026-05-03 / Codex

- Decision: Use `F1` as the runtime overlay toggle.
  Rationale: It gives a simple global debug switch without introducing configuration files or editor-only bootstrap.
  Date/Author: 2026-05-03 / Codex

- Decision: Respect ImGui `WantCaptureMouse`, `WantCaptureKeyboard`, and text input capture in gameplay input.
  Rationale: Interactive ImGui windows and text fields are not usable if gameplay systems continue to consume the same input.
  Date/Author: 2026-05-03 / Codex

## Outcomes & Retrospective

Milestone 1 outcome: raw input remains available for ImGui while gameplay-facing `Input` now has a shared capture gate independent of ImGui. The capture state lives in `Window.Core` so both `Graphics.Core` and `Input` can use it without creating a project reference cycle.

Milestone 2 outcome: ImGui renderer/device resources now have a lifecycle in `Graphics.Core` without changing scene submit order yet. Disabled overlay still produces no visible output and capture flags are cleared every graphics begin frame.

Milestone 3 outcome: scene command list submission now happens before ImGui command list submission, and `SwapBuffers()` is isolated in the last graphics system. This creates the render-order slot needed for user ImGui ECS systems without changing merge-thread behavior.

Initial usable overlay outcome: an ECS system can now call `ImGuiNET.ImGui` after `ImGuiBeginSystem` and before `ImGuiRenderSystem`. The built-in debug panel proves the path, and final runtime validation confirmed visibility and text entry.

Game project cleanup outcome: `DisplaySystem` uses the overlay-enabled guard before calling ImGui, and `MyGame.Client.Main` now references the same `ImGui.NET` version as the renderer package. This avoids loading a newer ImGui.NET API against a renderer compiled with the older binding.

Final validation outcome: `ClientLauncher` is the correct executable for the composed client module set. Running it with `KARPIK_IMGUI_ENABLED=1` produced a visible `Karpik Debug` ImGui window and the `DisplaySystem` sample `My Window` above the existing scene. Automated input clicked the debug panel `InputText` and typed text into it. Automated `F1` synthesis from the Codex tool environment did not reach SDL reliably, but the runtime toggle path is implemented over raw `IInputSource.KeyEvents` and remains the default user-facing way to enable the overlay.

## Context and Orientation

Relevant modules:

- `Modules/Client/Graphics/Graphics.Core` owns render ECS systems, `GraphicsContext`, `MergeThread`, and the current 2D command submission flow.
- `Modules/Client/Graphics/Graphics.OpenGL` creates the Veldrid `GraphicsDevice` and registers `IMergeThread` and pipelines.
- `Modules/Client/Window/Window.Core` defines `IWindow` and `IInputSource`.
- `Modules/Client/Window/Window.Sdl2` pumps SDL2/Veldrid window events into an `InputSnapshot`.
- `Modules/Client/Input` builds gameplay-facing input state from `IInputSource`.
- `first-parties/Kveldrid/src/Veldrid.ImGui` provides the existing Veldrid ImGui renderer.

Current render flow:

1. `GraphicsCoreBeginSystem.Run()` resizes the swapchain if needed and calls `GraphicsContext.BeginFrame()`.
2. Gameplay/client systems write draw commands into `GraphicsContext.Buffer`.
3. `GraphicsCoreMergeSystem.Run()` starts `MergeThread.BeginMerge()`.
4. `GraphicsCoreSubmitSystem.Run()` waits for merge completion, submits the scene command list, and calls `SwapBuffers()`.

For ImGui, the required frame flow is:

1. Window input is pumped first by `Window.Core`.
2. ImGui receives the raw Veldrid `InputSnapshot` and starts a new ImGui frame.
3. User ECS systems call `ImGuiNET.ImGui` directly.
4. The normal 2D scene is submitted.
5. ImGui draw data is rendered to a command list and submitted.
6. Swap buffers happens once at the end of the frame.

Definitions:

- ImGui overlay: immediate-mode UI rendered above the scene for debug tools and temporary UI.
- Raw input: the original `Veldrid.InputSnapshot` produced by the SDL2 window.
- Capture flags: ImGui IO flags such as `WantCaptureMouse` and `WantCaptureKeyboard` that indicate gameplay input should ignore the corresponding input for that frame.

## Real-Time Assessment

Hot path: yes. The integration touches render ECS systems and input update flow.

Allocation budget: ImGui is allowed to allocate while enabled because it is debug/temporary UI. The normal scene rendering path must remain unchanged when ImGui is disabled. Do not introduce new per-frame allocations into `MergeThread`, `GraphicsContext`, or gameplay draw command submission as part of this plan.

Data layout: ImGui is an immediate-mode object/API layer and should not become a gameplay data model. Long-lived gameplay UI state should remain outside this v1 integration.

Side boundary: all changes remain in `Client`. No `Server` or `Shared` project should reference ImGui or Veldrid.ImGui.

Tick behavior: ImGui uses render-frame `Time.DeltaTime`. This does not affect fixed-dt physics or simulation.

Concurrency: ImGui should run on the main/render thread around ECS render systems. Do not call ImGui APIs from the merge worker thread. The ImGui command list should be independent from `MergeThread`'s scene command list.

Validation: build affected client projects, manually validate overlay rendering/input/capture/resize, and check that `SwapBuffers()` is still called exactly once per frame.

## Plan of Work

First, expose raw input without coupling `Window.Core` to ImGui:

- Add `InputSnapshot Snapshot { get; }` to `IInputSource`.
- Implement it in `SDL2InputSource` by returning the latest `_snapshot`.
- Keep existing derived input APIs intact for compatibility.

Second, add a client input capture service:

- Add `InputCaptureState` in a client-accessible module without referencing ImGui.
- The state should expose booleans for mouse, keyboard, and text capture.
- `Input.Update()` should read this service and suppress gameplay-facing mouse/key/char updates when capture is active.
- Raw `IInputSource` must remain raw so ImGui can still receive input.

Third, add ImGui runtime integration in `Graphics.Core`:

- Add an `ImGuiOverlayState` service with `Enabled` and latest capture flag fields.
- Add an `ImGuiRenderContext` or equivalent service that owns `ImGuiRenderer`, the ImGui `CommandList`, and resize/update/render methods.
- Initialize `ImGuiRenderer` from the registered `GraphicsDevice`, main framebuffer `OutputDescription`, and current window size.
- On resize, call the renderer's resize method before rendering the next frame.

Fourth, split the final graphics submit step:

- Keep scene command submission in a system that waits for `IMergeThread` and submits the scene command list.
- Move `SwapBuffers()` into a separate final system.
- Insert ImGui rendering between scene submit and swap.

Fifth, add ECS systems for ImGui:

- `ImGuiBeginSystem` runs after window input has been pumped and after the graphics device/swapchain size is current.
- It toggles `ImGuiOverlayState.Enabled` on `F1`.
- If enabled, it calls `ImGuiRenderer.Update((float)Time.DeltaTime, inputSource.Snapshot)`.
- It records ImGui capture flags into `InputCaptureState` after the ImGui frame is active.
- `ImGuiRenderSystem` runs after user ImGui ECS systems and after scene submit, renders ImGui draw data, and submits the ImGui command list.
- If disabled, capture flags are cleared and no user ImGui frame is rendered.

Sixth, add a minimal built-in verification panel:

- Add a temporary/debug ECS system that draws a small ImGui window with frame timing, mouse position, and a text input field.
- Keep it clearly removable or guarded by the overlay enabled flag.
- Do not add docking, multi-viewport, texture browser, custom font management, or editor panels in v1.

## Milestones

Milestone 1: Raw input and capture plumbing.

Expected evidence:

- `IInputSource` exposes the latest `InputSnapshot`.
- `Input` can respect capture flags without referencing ImGui.
- Existing gameplay input compiles unchanged.

Validation:

- `dotnet build Modules/Client/Window/Window.Core/Window.Core.csproj`
- `dotnet build Modules/Client/Window/Window.Sdl2/Window.Sdl2.csproj`
- `dotnet build Modules/Client/Input/Input.csproj`

Milestone 2: ImGui renderer lifecycle.

Expected evidence:

- `Graphics.Core` registers and initializes an ImGui context/renderer.
- Resize updates the ImGui renderer display dimensions.
- Disabled overlay has no visible output and clears capture flags.

Validation:

- `dotnet build Modules/Client/Graphics/Graphics.Core/Graphics.Core.csproj`
- `dotnet build Modules/Client/Graphics/Graphics.OpenGL/Graphics.OpenGL.csproj`

Milestone 3: Render ordering and single swap.

Expected evidence:

- Scene command list is submitted first.
- ImGui command list is submitted second.
- `SwapBuffers()` is called once after both submissions.

Validation:

- Manual run: existing 2D rendering still appears.
- Manual run: ImGui appears above the scene when enabled.

Milestone 4: Input behavior.

Expected evidence:

- `F1` toggles the overlay.
- ImGui windows receive mouse and text input.
- When ImGui wants keyboard/mouse capture, gameplay `Input` does not receive those events.
- When overlay is disabled, gameplay input behaves as before.

Validation:

- Manual scene with an ImGui text input and an existing gameplay input action.

Milestone 5: Documentation and cleanup.

Expected evidence:

- Document how ECS systems should submit ImGui UI.
- Record final architecture trade-offs in this ExecPlan.
- Add an ADR only if the final implementation changes module ownership or render ordering in a durable way.

Validation:

- Final affected-project build commands pass.

## Concrete Steps

All commands assume working directory `C:\Users\artem\RiderProjects\KarpikEngine`.

Inspect current state:

- `dotnet build Modules/Client/Graphics/Graphics.Core/Graphics.Core.csproj`
- `dotnet build Modules/Client/Graphics/Graphics.OpenGL/Graphics.OpenGL.csproj`
- `dotnet build Modules/Client/Window/Window.Core/Window.Core.csproj`
- `dotnet build Modules/Client/Window/Window.Sdl2/Window.Sdl2.csproj`
- `dotnet build Modules/Client/Input/Input.csproj`

After Milestone 1:

- `dotnet build Modules/Client/Window/Window.Core/Window.Core.csproj`
- `dotnet build Modules/Client/Window/Window.Sdl2/Window.Sdl2.csproj`
- `dotnet build Modules/Client/Input/Input.csproj`

After Milestone 2 and 3:

- `dotnet build Modules/Client/Graphics/Graphics.Core/Graphics.Core.csproj`
- `dotnet build Modules/Client/Graphics/Graphics.OpenGL/Graphics.OpenGL.csproj`

Final validation:

- Run the client sample or game scene.
- Press `F1` and confirm ImGui appears/disappears.
- Interact with an ImGui text field.
- Confirm gameplay input is blocked only while ImGui capture flags are active.
- Resize the window and confirm ImGui remains correctly scaled and positioned.

## Validation and Acceptance

Acceptance criteria:

- ImGui is visible above the current 2D renderer when enabled.
- `F1` toggles the overlay at runtime.
- ImGui receives mouse, keyboard, wheel, and character input from the raw `InputSnapshot`.
- Gameplay input respects ImGui capture flags.
- Overlay disabled means no ImGui capture and no visible ImGui output.
- `SwapBuffers()` is called once per frame.
- All changes remain in client modules.
- No new ImGui dependency is introduced into `Shared` or `Server`.

Manual scenarios:

- Open a debug ImGui window and drag it.
- Type into an ImGui text field and confirm gameplay key bindings do not fire for captured input.
- Click outside ImGui or disable overlay and confirm gameplay input works.
- Resize the window and confirm ImGui coordinates match the new window size.

## Idempotence and Recovery

Safe to rerun:

- Build commands.
- Manual overlay toggling.
- Reinitializing the client after failed ImGui wiring.

Risky areas:

- Render ordering can accidentally call `SwapBuffers()` before ImGui rendering. Keep swap in a final dedicated system.
- Input capture can accidentally block raw input before ImGui sees it. Keep `IInputSource` raw and apply capture only in gameplay-facing `Input`.
- `ImGuiRenderer` can resize internal Veldrid buffers when UI draw volume grows. This is acceptable for debug UI, but do not treat enabled ImGui as a no-GC gameplay path.

Rollback:

- Disable ImGui systems and restore the original single `GraphicsCoreSubmitSystem` shape if render ordering becomes unstable.
- Keep `InputSnapshot` exposure if useful; it is a low-risk raw input accessor.
- Clear all capture flags when disabling ImGui to avoid stuck gameplay input.

## Artifacts and Notes

Potential follow-ups after v1:

- Dedicated debug panels for renderer stats, asset inspection, ECS world inspection, and profiling counters.
- Optional docking/editor mode as a separate ExecPlan.
- Allocation cleanup in `SDL2InputSource` and `Input`, independent of ImGui.
- Texture binding helpers for showing engine textures in ImGui via `ImGuiRenderer.GetOrCreateImGuiBinding`.
