# Texture source assets

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

**Goal:** PNG, JPG, and JPEG source assets receive stable editable `.meta` sidecars and build through the headless content pipeline as `texture` artifacts.

**Architecture:** `TextureProcessor` belongs to `Karpik.Content.Core`, validates encoded image bytes with `StbImageSharp`, and returns those same bytes as the cooked artifact. `ContentMetaTemplate` is the one shared extension-to-meta mapping used by the CLI and Editor; filesystem writes stay in those hosts. The Editor creates only missing supported sidecars while loading its Content tree. No Client Graphics, GPU, or runtime `AssetRef<ITexture2D>` dependency is introduced.

**Tech Stack:** .NET 10, `StbImageSharp` 2.30.15, System.Text.Json, xUnit v3, Avalonia.

**Spec:** `docs/02_ADR/content-pipeline-texture-source-format.md`.

## Global Constraints

- Accept only `.png`, `.jpg`, and `.jpeg` as `texture`; preserve `.json` as `raw-json`.
- Generated `.meta` uses namespace `game`, an immutable new lower-case GUID `assetId`, empty `importSettings`, and empty `dependencies`.
- Existing `.meta` files must never be overwritten; all fields except `assetId` remain editable authoring data.
- Texture cooking validates with `StbImageSharp` but preserves original encoded bytes.
- Core must not reference Graphics, Veldrid, GPU objects, Client, Server, ECS, or runtime texture types.
- This work is editor startup and build tooling only, never a frame, ECS, serialization, or networking hot path.

## Purpose / Big Picture

A developer can place `Content/Sprites/player.png`, open the Editor, and find a new adjacent `player.png.meta` containing `declaredType: "texture"` and a stable `assetId`. `content validate` and `content build` accept a valid PNG, JPG, or JPEG and publish its encoded bytes as a content-addressed cooked artifact. Editing supported `.meta` fields changes the build input; the Editor does not overwrite it on its next launch.

## Progress

- [x] (2026-09-13) ADR and scope approved: headless texture build support only.
- [x] (2026-09-13) Added and observed red Core template, processor, and mixed-build tests.
- [x] (2026-09-13) Implemented and default-registered Core texture support.
- [x] (2026-09-13) CLI and Editor create only missing supported sidecars through the shared template.
- [x] (2026-09-13) Content tests passed (61/61); Editor tests passed (102/102, 8 skipped); Content Tool and Editor Debug builds passed without warnings.

## Surprises & Discoveries

- Observation: `Graphics.Core` already uses `StbImageSharp` 2.30.15 for its legacy loose-file texture loader, but `Karpik.Content.Core` has no package reference.
  Evidence: `Modules/Client/Graphics/Graphics.Core/AssetManagement/Loaders/TextureLoader.cs` and `Graphics.Core.csproj`.
- Observation: `ContentBuildCoordinator` defaults to `RawJsonProcessor` only, and the CLI duplicates its JSON-only meta template.
  Evidence: `Karpik.Content.Core/ContentBuildCoordinator.cs` and `Karpik.Content.Tool/Program.cs`.
- Observation: `ProjectViewModel` currently reads Content and hides `.meta` files but has no Core project reference or write path.
  Evidence: `Karpik.Editor/ViewModels/EditorShellViewModel.cs` and `Karpik.Editor/Karpik.Editor.csproj`.
- Observation: `StbImageSharp.ImageResult.FromStream` signals malformed image bytes with `InvalidOperationException`.
  Evidence: `TextureProcessorTests.Process_MalformedImage_ReportsError` before the processor boundary handled that exception.
- Observation: the former unsupported-type test used `texture` as a placeholder declared type.
  Evidence: `ContentBuildCoordinatorTests.Validate_DetectsUnsupportedType`; its sentinel is now `unsupported-test` because `texture` is supported.

## Decision Log

- Decision: Cook unchanged encoded PNG/JPEG bytes in this slice.
  Rationale: content build support and stable identities do not require GPU allocation, pixel conversion, or a Client dependency.
  Date/Author: 2026-09-13 / developer and Codex.
- Decision: Put extension mapping and serialized initial meta in one Core `ContentMetaTemplate` API; leave create-only disk writes in CLI and Editor.
  Rationale: two hosts must produce byte-equivalent authoring sidecars, while Core stays independent from filesystem orchestration.
  Date/Author: 2026-09-13 / developer and Codex.
- Decision: Start generated logical names with `game/`.
  Rationale: this matches current docs and CLI examples; a project-level namespace setting is deferred.
  Date/Author: 2026-09-13 / developer and Codex.
- Decision: Treat per-file Editor sidecar write failures as non-fatal to the asset tree.
  Rationale: project browsing remains useful when one asset is locked or inaccessible; a later Inspector or diagnostics pane can expose per-file details.
  Date/Author: 2026-09-13 / developer and Codex.

## Outcomes & Retrospective

Implemented the approved headless texture source slice. The Core template maps
JSON and PNG/JPG/JPEG consistently for CLI and Editor. Texture build validates
the image with StbImageSharp and publishes unchanged encoded bytes. No runtime
texture API, GPU resource, or Client dependency was introduced.

## Context and Orientation

`Karpik.Content.Core/AssetMeta.cs` parses and canonicalizes sidecars. It currently exposes `ExpectedDeclaredTypeRawJson`. `IContentProcessor` has `DeclaredType`, `Version`, and `Process(ReadOnlySpan<byte>, AssetMeta, string)`. `RawJsonProcessor` validates JSON and emits canonical JSON bytes. `ContentBuildCoordinator` scans every non-meta source file, requires `<source>.meta`, resolves the declared type through its processor map, then hashes source, canonical meta, and processor version to make artifacts.

`Karpik.Content.Tool/Program.cs` implements `create`; it currently rejects all non-JSON files and writes its own JSON template with `FileMode.CreateNew`. `Karpik.Editor/ViewModels/EditorShellViewModel.cs` has `ProjectViewModel.LoadAssets`, which reads `<solution directory>/Content`, recurses it, and hides `.meta` entries. `Karpik.Editor` does not yet reference `Karpik.Content.Core`.

`Modules/Client/Graphics/Graphics.Core/AssetManagement/Loaders/TextureLoader.cs` uses `StbImageSharp` to decode PNG/JPEG for the legacy path-based loader. It is evidence for the package version and accepted image formats, not a dependency to import into Core.

## Real-Time Assessment

No edited code executes in `Update`, `FixedUpdate`, ECS systems, render loops, serialization loops, or network pumps. Image validation and sidecar creation allocate and read/write files during content validation/build and Editor project load; this is acceptable outside hot paths. `TextureProcessor` remains Core-only, preserving Client/Server/Shared boundaries and adding no concurrency or GPU lifetime ownership. Validation consists of deterministic unit and integration tests; no benchmark or allocation test is required.

## Plan of Work

First add Core tests that define extension mapping, meta immutability-by-non-overwrite, valid PNG/JPEG processing, malformed-image rejection, and a mixed build. Then add `ContentMetaTemplate`, `TextureProcessor`, the Core package reference, the `texture` constant, and default coordinator registration.

Next change the CLI to call the shared template instead of having a JSON-only branch. Add a Core reference to Editor and invoke the same template while recursively scanning Content; use `FileMode.CreateNew`, ignore a concurrent already-created sidecar or an inaccessible individual file, and retain the existing hidden-tree behavior. Finally update practical content documentation, run focused and complete tests, and record results in this plan.

## Milestones

### Milestone 1: Core texture contract

Add red tests under `Karpik.Content.Tests` for `ContentMetaTemplate.TryCreate(relativePath, "game", out string metaJson)`, `TextureProcessor`, and a mixed JSON/image build. The API returns `false` for unsupported extensions and `true` with parseable meta for `.json`, `.png`, `.jpg`, and `.jpeg`. `TextureProcessor` returns identical input bytes for valid PNG/JPEG and a `ContentDiagnosticSeverity.Error` result for malformed input.

Run from `C:\Users\artem\RiderProjects\KarpikEngine`:

    dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~ContentMetaTemplateTests|FullyQualifiedName~TextureProcessorTests|FullyQualifiedName~ContentBuildCoordinatorTests"

Expected before implementation: compile failure for the new template/processor or failing assertions because `texture` is unsupported.

### Milestone 2: Core implementation

Create `Karpik.Content.Core/ContentMetaTemplate.cs` and `Karpik.Content.Core/TextureProcessor.cs`. Add the direct `StbImageSharp` package reference to `Karpik.Content.Core/Karpik.Content.Core.csproj`, define `AssetMeta.ExpectedDeclaredTypeTexture`, and default-register `TextureProcessor` in `ContentBuildCoordinator`. Validate image bytes with `ImageResult.FromMemory` or the equivalent supported StbImageSharp API and preserve `sourceBytes.ToArray()` only after successful decoding.

Rerun Milestone 1. Expected: all focused tests pass, and a mixed build emits an entry with `declaredType: "texture"`.

### Milestone 3: Shared sidecar creation hosts

Update `Karpik.Content.Tool/Program.cs` so `create` accepts all template-supported extensions and writes the exact returned template JSON using its existing create-only stream. Update `Karpik.Editor/Karpik.Editor.csproj` with a Core project reference. In `ProjectViewModel`, scan `Content` for source files before building the tree; call `ContentMetaTemplate.TryCreate` with `game`, then create `<source>.meta` using `FileMode.CreateNew`. Keep existing sidecars untouched, skip unsupported extensions, and keep rendering the tree when an individual write fails.

Add tests for CLI image creation and Editor generation/preservation. Run targeted Content and Editor tests. Expected: fresh PNG/JPEG sources receive sidecars once; existing `assetId` remains unchanged.

### Milestone 4: Documentation and final validation

Update `docs/content-pipeline.md` to list `texture` and PNG/JPG/JPEG support, explain that texture artifacts currently preserve encoded bytes, and clarify that import settings are editable but have no texture behavior yet. Run all Content tests, the focused Editor tests, and Debug builds of Content Tool and Editor. Check the diff for whitespace errors. Record results in `Progress` and `Outcomes & Retrospective`.

## Concrete Steps

1. Create `Karpik.Content.Tests/ContentMetaTemplateTests.cs` with parameterized paths `config/player.json`, `Sprites/player.png`, `Sprites/player.jpg`, and `Sprites/player.jpeg`. Parse each success result with `AssetMeta.Parse`; assert `raw-json` for JSON, `texture` for images, a valid `AssetId`, `game/<path-without-extension>`, `{}` settings, and no dependencies. Add an unsupported `player.gif` case that returns `false`.
2. Add valid one-pixel PNG and JPEG byte fixtures to `Karpik.Content.Tests/TextureProcessorTests.cs`; assert `TextureProcessor.Process` returns byte-identical cooked bytes and no error diagnostics. Add malformed bytes and assert an error diagnostic. Add the processor first only to tests so the initial command is red.
3. Extend `Karpik.Content.Tests/ContentBuildCoordinatorTests.cs` with a source PNG and generated `texture` meta; assert `Build` succeeds and yields a manifest entry for `texture`. Run Milestone 1 command and record the red result.
4. Add `ExpectedDeclaredTypeTexture = "texture"` to `AssetMeta`. Implement public static `ContentMetaTemplate.TryCreate(string relativePath, string assetNamespace, out string metaJson)`: map extension case-insensitively, derive the normalized slash-separated extensionless path, create `AssetId.New()`, serialize ordered initial fields, and return `false` without assigning usable JSON for unsupported paths.
5. Add `TextureProcessor : IContentProcessor` with `DeclaredType == AssetMeta.ExpectedDeclaredTypeTexture` and a fixed `Version`. Reject an extension outside PNG/JPG/JPEG or decoding failures with an error diagnostic; otherwise return a new byte array containing the unchanged source bytes and no dependencies.
6. Add `StbImageSharp` 2.30.15 directly to `Karpik.Content.Core.csproj` and add `new TextureProcessor()` to the default coordinator processor list. Run Milestone 1 command and record its green result.
7. Change `RunCreate` in `Karpik.Content.Tool/Program.cs` to call `ContentMetaTemplate.TryCreate(relativePath, ns, out string metaJson)` and retain its existing create-only write and messages. Add CLI tests for `.png`, `.jpg`, `.jpeg`, and unsupported extensions.
8. Add the Core project reference in `Karpik.Editor/Karpik.Editor.csproj`. Add a private `EnsureMetaFiles(string contentPath)` to `ProjectViewModel`; it enumerates files, skips sidecars, gets a template from Core with `game`, and creates only a missing `<source>.meta`. Handle `IOException`/`UnauthorizedAccessException` per file and continue. Call it before `LoadDirectory`.
9. Extend `Karpik.Editor.Tests/ProjectViewModelTests.cs` with a PNG source: verify the generated sidecar parses as `texture`, is not shown in the tree, and survives a second `Path` assignment byte-for-byte. Run:

    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~ProjectViewModelTests"

10. Update `docs/content-pipeline.md`. Run:

    dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj --no-restore -m:1 -nr:false

    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~ProjectViewModelTests"

    dotnet build Karpik.Content.Tool\Karpik.Content.Tool.csproj --no-restore -m:1 -nr:false

    dotnet build Karpik.Editor\Karpik.Editor.csproj --no-restore -m:1 -nr:false

    git diff --check

## Validation and Acceptance

Acceptance requires all of the following:

- A `.png`, `.jpg`, or `.jpeg` source receives `declaredType: "texture"`; JSON still receives `raw-json`.
- `TextureProcessor` accepts valid PNG/JPEG, rejects malformed images, and returns original encoded bytes.
- `content validate` and `content build` accept a mixed JSON/image Content root.
- CLI and Editor generate byte-valid sidecars through the same Core contract.
- A second Editor scan never changes an existing sidecar or its `assetId`; sidecars remain hidden in the Project tree.
- Full Content tests, focused Editor tests, Content Tool build, Editor build, and `git diff --check` exit successfully.
- No project imports Graphics.Core, Veldrid, or a runtime texture API into Content Core.

## Idempotence and Recovery

All tests and builds are safe to rerun. Sidecar creation uses create-only semantics, so rerunning Editor or CLI preserves existing files. If a malformed image has a generated sidecar, fix or remove the source file; do not replace its `assetId` merely to correct image bytes. Rollback is limited to the Content Core processor/template, CLI, Editor scan, tests, docs, and this plan; it does not alter published SDK installations or external game source trees.

## Artifacts and Notes

- Design authority: `docs/02_ADR/content-pipeline-texture-source-format.md`.
- Existing general contract: `docs/02_ADR/content-pipeline-build-contract.md`.
- Existing user documentation: `docs/content-pipeline.md`.
- No binary texture fixture is added to the repository; tests construct their small image byte arrays in source.
