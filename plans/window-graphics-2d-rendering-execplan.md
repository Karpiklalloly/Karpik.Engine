# Extend Window and Graphics 2D Rendering

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Extend `Modules/Client/Window` and `Modules/Client/Graphics` so the 2D renderer can draw rotated primitives and textures, render text through an atlas/SDF path, draw either in window/screen coordinates or game world coordinates, and support cameras.

The observable outcome is a client sample or test scene where:

- a rectangle and texture rotate around a specified origin;
- text is rendered from a loaded font without per-frame glyph rasterization;
- one draw call path uses pixel/window coordinates and another uses world coordinates;
- moving or zooming a camera changes world-space rendering without changing UI/screen-space rendering.

## Progress

- [x] (2026-05-02 14:56 +04:00) Initial plan created after inspecting the current `Window` and `Graphics` modules.
- [x] (2026-05-02 16:03 +04:00) Drafted first Milestone 1 slice: added draw-space/transform value types, rotation/origin fields on rect/texture commands, and screen-space rotated quad emission in `MergeThread`.
- [x] (2026-05-02 16:13 +04:00) Added no-GC convenience command-buffer helpers for top-left and centered rect/texture drawing while keeping low-level command position semantics unchanged.
- [x] (2026-05-02 16:20 +04:00) Moved screen-space quad transform math into an internal helper so merge stays focused on batching and the math can be unit-tested separately.
- [x] (2026-05-02 16:31 +04:00) Added initial `Camera2D` value type and `GraphicsCameraState` service with per-frame snapshot capture, without yet applying the camera in merge.
- [x] (2026-05-02 16:43 +04:00) Applied captured `Camera2D` to `DrawSpace.World` rect/texture commands in merge while leaving `DrawSpace.Screen` on the existing pixel-to-clip path.
- [x] (2026-05-02 17:02 +04:00) Moved camera snapshot capture to `MergeThread.BeginMerge()` and fixed normalization so missing viewport defaults do not reset camera position.
- [x] (2026-05-02 17:10 +04:00) Added viewport-less `Camera2D.CreateDefault()` and updated the sample camera initialization to use it.
- [x] (2026-05-02 17:14 +04:00) Added `Camera2D.WorldToScreen` and `ScreenToWorld` helpers and reused `WorldToScreen` from quad projection.
- [x] (2026-05-02 17:24 +04:00) Fixed `SDL2Window.Exists` and one-shot `IsResized` state instead of exposing never-updated auto-properties.
- [x] (2026-05-02 17:35 +04:00) Added optional normalized source UV rectangle for texture commands and helpers; default still renders the full texture.
- [x] (2026-05-02 17:48 +04:00) Added a dependency-light source-linked console test runner for camera and quad transform math.
- [ ] Add transform and coordinate-space API to draw commands.
- [ ] Add camera data model and camera state service.
- [ ] Add SDF/MSDF font asset loading and text command batching.
- [ ] Update merge/render path and shaders.
- [ ] Add validation tests, sample scene, and allocation checks.

## Surprises & Discoveries

- Observation: `DrawTextCmd` already exists in `Graphics.Core`, but `MergeThread.BuildCommandList` never handles `DrawCommandType.Text`.
  Evidence: `Modules/Client/Graphics/Graphics.Core/Commands/DrawTextCmd.cs` defines the command, while `Modules/Client/Graphics/Graphics.Core/Merge/MergeThread.cs` switches only over `Rect` and `Texture`.

- Observation: current 2D vertices are already emitted in clip-space CPU-side, so adding cameras/world coordinates by only changing public command fields would push more transform math into the merge hot path.
  Evidence: `MergeThread.AddRectToBatch` and `MergeThread.AddTextureToBatch` convert pixel coordinates directly to normalized device coordinates using framebuffer width/height.

- Observation: current command buffers resize managed arrays when capacity is exceeded. This is acceptable during warm-up but violates no-GC expectations if command volume grows during gameplay.
  Evidence: `ThreadBuffer.Resize<T>` calls `Array.Resize`.

- Observation: `IWindow.IsResized` and `IWindow.Exists` are exposed but the SDL2 implementation returns default values because they are read-only auto-properties never updated.
  Evidence: `Modules/Client/Window/Window.Sdl2/SDL2Window.cs`.

- Observation: baseline `dotnet build Modules/Client/Graphics/Graphics.Core/Graphics.Core.csproj` currently exits with code 1 while reporting zero warnings and zero errors, both before and after the first Milestone 1 slice.
  Evidence: MSBuild output only prints project restore/build start followed by `Ошибка сборки. Предупреждений: 0 Ошибок: 0`.

- Observation: camera normalization originally replaced `ActiveCamera` with a default camera when viewport size was zero, discarding caller-updated `Position`.
  Evidence: `DisplaySystem` creates `Camera2D.CreateDefault(0, 0)` and then moves `_camera.Position`; the old `GraphicsCameraState.CaptureForFrame` reset that active camera before merge.

## Decision Log

- Decision: Treat this as a staged renderer architecture change, not four isolated feature patches.
  Rationale: Rotation, world coordinates, and cameras all depend on the same transform path. Text depends on batching, atlas lifetime, and pipeline/resource binding decisions. Implementing them separately would duplicate transform code and make the render hot path harder to keep allocation-free.
  Date/Author: 2026-05-02 / Codex

- Decision: Keep `Window` responsible for surface size, framebuffer resize events, DPI/content scale, and input-space conversion only; keep camera/world transforms in `Graphics`.
  Rationale: `Window` should not know game-world concepts. `Graphics` already owns framebuffer dimensions, render commands, pipelines, and merge timing.
  Date/Author: 2026-05-02 / Codex

- Decision: Use a small value-type transform/camera model rather than class-based scene nodes.
  Rationale: Rendering commands are written in hot paths and later merged linearly. Class hierarchies would add pointer chasing, lifetime complexity, and avoidable GC pressure.
  Date/Author: 2026-05-02 / Codex

- Decision: Start text with a prebuilt atlas path and leave dynamic glyph packing as a later milestone.
  Rationale: Rasterizing or packing glyphs during `Run` or merge would allocate and create unpredictable latency. A prebuilt SDF/MSDF atlas gives deterministic render work and a clear asset pipeline.
  Date/Author: 2026-05-02 / Codex

## Outcomes & Retrospective

No outcome yet. Update this after each major milestone and at completion.

## Context and Orientation

Relevant modules:

- `Modules/Client/Window/Window.Core` defines `IWindow` and update systems.
- `Modules/Client/Window/Window.Sdl2` adapts Veldrid SDL2 windows.
- `Modules/Client/Graphics/Graphics.Core` owns render commands, command buffers, merge thread, Veldrid resources, and ECS render systems.
- `Modules/Client/Graphics/Graphics.OpenGL` creates the OpenGL `GraphicsDevice` and registers `MergeThread` and `Preset2DPipeline`.

Current render flow:

1. `GraphicsCoreBeginSystem.Run` resizes the swapchain if the window size changed and calls `GraphicsContext.BeginFrame`.
2. Gameplay/client code writes `DrawRectCmd`, `DrawTextureCmd`, and currently possible but unused `DrawTextCmd` into `GraphicsContext.Buffer`.
3. `GraphicsCoreMergeSystem.Run` starts `MergeThread.BeginMerge`.
4. `MergeThread.BuildCommandList` collects pending buffers, emits vertices into `MergeContext.Vertices`, updates a dynamic Veldrid vertex buffer, and builds a `CommandList`.
5. `GraphicsCoreSubmitSystem.Run` waits for merge completion, submits the command list, and swaps buffers.

Important current files:

- `Modules/Client/Graphics/Graphics.Core/ICommandBuffer.cs`
- `Modules/Client/Graphics/Graphics.Core/Buffers/ThreadBuffer.cs`
- `Modules/Client/Graphics/Graphics.Core/Commands/DrawRectCmd.cs`
- `Modules/Client/Graphics/Graphics.Core/Commands/DrawTextureCmd.cs`
- `Modules/Client/Graphics/Graphics.Core/Commands/DrawTextCmd.cs`
- `Modules/Client/Graphics/Graphics.Core/Vertex2D.cs`
- `Modules/Client/Graphics/Graphics.Core/Merge/MergeThread.cs`
- `Modules/Client/Graphics/Graphics.Core/Presets/Preset2DPipeline.cs`
- `Modules/Client/Window/Window.Core/IWindow.cs`
- `Modules/Client/Window/Window.Sdl2/SDL2Window.cs`

Definitions:

- Window/screen space: coordinates in framebuffer pixels, usually origin at top-left.
- World space: game coordinates independent of the current window size. These coordinates are transformed by a camera before reaching clip space.
- Camera: value-type state that maps world coordinates to a viewport. For 2D, the initial model should include position, rotation, zoom, viewport rectangle, and projection mode.
- SDF/MSDF text: text rendering where glyph shape is stored in a distance-field texture atlas. The shader reconstructs crisp edges at draw time.

## Real-Time Assessment

Hot path: yes. The edited code will be called from ECS `Run`, render frame begin/merge/submit, and possibly gameplay draw systems.

Allocation budget: zero managed allocations after warm-up for draw command submission, merge, and submit. Acceptable allocations are limited to initialization, asset loading, font atlas creation, pipeline creation, and explicit capacity growth during loading screens or debug-only paths. `ThreadBuffer` capacity should become configurable/prewarmed or expose a no-resize failure mode for release builds.

Data layout: commands remain structs in dense arrays. Camera state should be a small struct snapshot copied once per frame. Text glyph quads should be emitted linearly from atlas metadata into the existing quad batch path. Avoid per-character objects, LINQ, `Dictionary` lookup in merge, closures, and managed strings in submitted hot-path commands.

Side boundary: all changes stay in `Client`. No `Server` dependency is allowed. Shared math types may use `System.Numerics`, but graphics resources and Veldrid types must not leak into `Shared` or `Server`.

Tick behavior: rendering may use render-frame interpolation data, but game simulation and physics camera targets must still be updated from fixed dt systems where relevant. This plan does not change physics tick rules.

Concurrency: `MergeThread` builds command lists on its worker thread. Camera/framebuffer state must be captured before signaling the worker, then treated as immutable for that merge. Text/font resources used by merge must have stable lifetime until the submitted command list is complete.

Validation: build checks, focused unit tests for transform math, render sample/manual test, and allocation measurements around command submission and merge. GPU visual verification should include rotated quads, camera movement, zoom, and text at multiple sizes.

## Plan of Work

First, introduce explicit 2D transform and coordinate-space data:

- Add `DrawSpace` or similar enum in `Graphics.Core`, with `Screen` and `World`.
- Add a compact `DrawTransform2D` struct with position, size, origin, rotation radians, and optional source rectangle for textures.
- Update `DrawRectCmd` and `DrawTextureCmd` to carry rotation/origin/space without introducing class options objects.
- Prefer radians internally. Public helpers may provide degrees outside the hot path, but stored commands should avoid repeated conversion.

Second, move coordinate conversion into a reusable math layer:

- Add an internal `Transform2D` helper in `Graphics.Core` that computes four quad corners from position/size/origin/rotation.
- Add camera/screen projection helpers that convert world or screen coordinates to clip-space.
- Keep this code deterministic and allocation-free. Use `Vector2`, `Matrix3x2`, or direct sin/cos math. Direct math may be preferable in the merge hot path if profiling shows `Matrix3x2` overhead.

Third, add cameras:

- Add `Camera2D` as a public struct in `Graphics.Core` with position, rotation, zoom, viewport rectangle, and pixels-per-unit or orthographic size.
- Add an `ICameraProvider` or `GraphicsCameraState` service registered by `Graphics.Core`/`Graphics.OpenGL`.
- Add a default camera that maps world units predictably to the current framebuffer.
- Capture active camera state in `GraphicsContext.BeginFrame` or `MergeThread.BeginMerge`, not during per-command writes.
- Support screen-space commands bypassing the camera for UI.

Fourth, update Window integration:

- Fix or clarify `IWindow.Exists` and `IWindow.IsResized` behavior in `Window.Sdl2`.
- Add DPI/content-scale support only if needed by text sizing and logical coordinates. If added, keep it in `Window.Core` as surface information, not camera logic.
- Prefer the existing `Resized` event or framebuffer dimensions captured from Veldrid for projection updates.

Fifth, implement text as an atlas-backed render path:

- Replace empty `IFont` with a font resource contract that exposes atlas texture, line metrics, glyph lookup, and distance-field scale/range.
- Add `FontAsset` and loader support. Start with a prebuilt atlas plus metrics file, or a build-time generated atlas from `.ttf`. Runtime `.ttf` rasterization should happen only during asset loading.
- Change `DrawTextCmd` so hot-path text submission does not depend on unstable `ReadOnlyMemory<char>` unless lifetime is explicit. Preferred options:
  - for static/debug text, use a `TextRun`/handle stored in a text cache;
  - for dynamic text, provide a caller-owned `ReadOnlySpan<char>` API that copies into a preallocated per-thread char buffer with capacity checks;
  - avoid storing raw `string` as the only long-term command contract.
- Add `DrawCommandType.Text` handling in `MergeThread.BuildCommandList`.
- Emit one quad per glyph into the same vertex/index batch, flushing on font atlas texture or text pipeline changes.
- Add a dedicated SDF/MSDF fragment shader and `Preset2DPipeline.TextPipeline`.

Sixth, update pipelines and shaders:

- Keep rect and texture paths compatible with current `Vertex2D` initially.
- Add text shader assets, likely `Shaders/TextSdf.frag` and reuse or extend `Shaders/2D.vert`.
- If camera matrix is moved to GPU later, add a uniform/resource layout. For the first implementation, CPU-side clip-space emission is simpler but does more CPU work per vertex. Record profiling results before moving transforms to the shader.

Seventh, add public convenience API only after the command structs are stable:

- Keep `ICommandBuffer.Add(in DrawRectCmd)` and `Add(in DrawTextureCmd)` as the lowest-level API.
- Add optional extension methods for common draw calls, but avoid many overloads that hide allocations or coordinate-space defaults.
- Document the default spaces: UI/screen-space by default for existing behavior, world-space only when explicitly requested or through a world drawing helper.

## Milestones

Milestone 1: Transform and rotation for existing screen-space drawing.

Expected evidence:

- `DrawRectCmd` and `DrawTextureCmd` can specify origin and rotation.
- Existing non-rotated draw behavior remains unchanged.
- `MergeThread` emits rotated quads correctly in screen coordinates.

Milestone 2: Camera and world-space rendering.

Expected evidence:

- A world-space command changes visual position when the active camera moves or zooms.
- Screen-space commands remain fixed in window pixels.
- Resize updates projection without stretching world rendering unexpectedly.

Milestone 3: Text resource model and atlas loading.

Expected evidence:

- `IFont` has concrete atlas/metrics semantics.
- Font assets load outside the render hot path.
- Text commands can be submitted without per-frame glyph rasterization.

Milestone 4: Text rendering pipeline.

Expected evidence:

- `DrawCommandType.Text` is handled by merge.
- Text renders at multiple sizes using SDF/MSDF shader.
- Text batching flushes only on pipeline/font atlas changes or capacity.

Milestone 5: Real-time validation.

Expected evidence:

- Build succeeds.
- Transform math tests pass.
- Allocation checks show no hot-path allocations for prewarmed command buffers and loaded fonts.
- Manual or automated render sample demonstrates rotated rect/texture, camera, world/screen split, and text.

## Concrete Steps

All commands assume working directory `C:\Users\artem\RiderProjects\KarpikEngine`.

1. Inspect current build state:

   `dotnet build Modules/Client/Graphics/Graphics.Core/Graphics.Core.csproj`

   Expected: project builds before feature edits, or failures are recorded as pre-existing.

2. Add transform structs and command fields in:

   - `Modules/Client/Graphics/Graphics.Core/Commands/DrawRectCmd.cs`
   - `Modules/Client/Graphics/Graphics.Core/Commands/DrawTextureCmd.cs`
   - new `Modules/Client/Graphics/Graphics.Core/DrawSpace.cs`
   - new `Modules/Client/Graphics/Graphics.Core/DrawTransform2D.cs`

3. Refactor quad emission in:

   - `Modules/Client/Graphics/Graphics.Core/Merge/MergeThread.cs`
   - optional new internal helper under `Modules/Client/Graphics/Graphics.Core/Merge/`

4. Add camera service/types in:

   - new `Modules/Client/Graphics/Graphics.Core/Camera/Camera2D.cs`
   - new `Modules/Client/Graphics/Graphics.Core/Camera/GraphicsCameraState.cs`
   - `Modules/Client/Graphics/Graphics.Core/GraphicsCoreInstaller.cs` or `Modules/Client/Graphics/Graphics.OpenGL/GraphicsOpenGLInstaller.cs`
   - `Modules/Client/Graphics/Graphics.Core/ECS/GraphicsCoreBeginSystem.cs`

5. Fix window resize state if required:

   - `Modules/Client/Window/Window.Core/IWindow.cs`
   - `Modules/Client/Window/Window.Sdl2/SDL2Window.cs`

6. Add font resources and asset loading:

   - `Modules/Client/Graphics/Graphics.Core/Resources/IFont.cs`
   - new font asset/loader files under `Modules/Client/Graphics/Graphics.Core/AssetManagement/`
   - project file updates for font atlas/metrics assets if needed.

7. Add shader/pipeline support:

   - `Modules/Client/Graphics/Graphics.Core/Presets/Preset2DPipeline.cs`
   - shader assets under the existing shader asset root used by `ShaderLoader`
   - `Modules/Client/Graphics/Graphics.OpenGL/GraphicsOpenGLInstaller.cs` if preloading shader handles remains necessary.

8. Build:

   `dotnet build Modules/Client/Graphics/Graphics.Core/Graphics.Core.csproj`

   `dotnet build Modules/Client/Graphics/Graphics.OpenGL/Graphics.OpenGL.csproj`

   `dotnet build Modules/Client/Window/Window.Core/Window.Core.csproj`

   `dotnet build Modules/Client/Window/Window.Sdl2/Window.Sdl2.csproj`

9. Add tests or a focused sample project. If no local test project exists for these modules, create one rather than relying only on manual inspection.

10. Run validation:

   `dotnet test <new-or-existing-graphics-tests>.csproj`

   Expected: transform/camera/font metrics tests pass.

## Validation and Acceptance

Acceptance criteria:

- Existing screen-space rectangle and texture commands still render as before when rotation is zero and origin is default.
- Rotation works around top-left, center, and caller-specified origin.
- World-space commands are transformed by the active camera; screen-space commands are not.
- Camera supports position and zoom in the first implementation; camera rotation can be included in the data model and must be validated if implemented in the first pass.
- Text renders from an atlas-backed font resource. No glyph rasterization happens in `Run`, `BeginMerge`, `BuildCommandList`, or submit.
- `DrawTextCmd` is no longer a dead command type.
- Prewarmed command submission and merge do not allocate managed memory per frame.
- Resizing the window updates framebuffer/projection and does not require recreating per-frame managed objects.

Validation methods:

- Unit tests for coordinate conversion:
  - screen pixel to clip-space;
  - rotated quad corners;
  - world to screen/clip under camera position and zoom;
  - viewport resize behavior.

- Font tests:
  - metrics load correctly;
  - missing glyph fallback is deterministic;
  - text layout for a simple ASCII string emits the expected glyph count and advances.

- Manual render scene:
  - one rotating colored rectangle;
  - one rotating texture;
  - one world-space grid/object controlled by camera movement/zoom;
  - one screen-space overlay unaffected by camera;
  - one SDF/MSDF text sample at small and large sizes.

- Allocation checks:
  - command submission into prewarmed buffers;
  - merge of rects/textures/text using loaded resources;
  - camera update and projection capture.

## Idempotence and Recovery

Most steps are safe to rerun:

- Builds and tests are idempotent.
- Shader and asset loading changes can be validated independently.
- Camera and transform math can be unit-tested without GPU access.

Risky areas:

- Changing command struct layout can break downstream call sites. Keep old defaults equivalent and update all compile errors deliberately.
- Text command lifetime is easy to get wrong. Do not store transient spans or caller-owned mutable memory unless the API makes lifetime explicit.
- Merge thread resource lifetime must outlive command list submission. Font atlases and texture resource sets should be disposed only when no pending frame can reference them.
- Array resizing in `ThreadBuffer` can hide allocations. If capacity is exceeded during validation, either prewarm or add explicit capacity configuration before accepting the change.

Rollback:

- Transform/camera changes can be reverted by restoring old command fields and old `AddRectToBatch`/`AddTextureToBatch` coordinate conversion.
- Text can be isolated behind `DrawCommandType.Text`; if text pipeline is unstable, keep rect/texture support merged and disable text command submission until resource lifetime and shader behavior are fixed.

## Artifacts and Notes

This plan intentionally does not introduce a `Server` or `Shared` dependency. Rendering remains a `Client` concern.

Potential ADR after implementation: `docs/02_ADR/<date>-graphics-2d-camera-and-text-rendering.md`, if the final design locks in CPU-side versus GPU-side transforms or a specific SDF/MSDF font pipeline.
