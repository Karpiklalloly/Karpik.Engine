# Package content formats for external game templates

This ExecPlan is a living document. It must be maintained according to
`plans/PLANS.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:executing-plans` to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an externally created Karpik game build `.font-json`, `.vert`,
and `.frag` assets, publish cooked content into its runtime bundle, and do so
using only the installed SDK package.

**Architecture:** `Karpik.Content.Core` gains two headless processors whose
outputs are validated source bytes. `Karpik.Engine.Sdk` packages the CLI and
source generator, invokes them by package-relative paths, and copies cooked
content to `TargetDir` before bundle creation. The game template opts in from
both runtime projects and contains stable sidecars for every bundled supported
asset.

**Tech Stack:** .NET 10, MSBuild SDK targets, Roslyn analyzer, xUnit v3,
System.Text.Json, StbImageSharp, NuGet packing.

**Spec:** `docs/02_ADR/content-pipeline-texture-source-format.md`

## Global Constraints

- `font-json` validates JSON and keeps source bytes; it does not create a
  runtime font loader.
- `shader` accepts only `.vert` and `.frag`, validates non-empty strict UTF-8,
  and keeps source bytes; it never compiles or reflects GLSL in Core.
- `texture` remains headless and Core must not depend on `Graphics.Core`,
  Veldrid, Client, or Server.
- SDK consumers must not need `KarpikRepositoryRoot`, a repository project
  reference, or a checkout to build content.
- Cooked `manifest.json` and `artifacts/` are copied alongside existing source
  assets in `$(TargetDir)Content`; legacy source files remain until runtime
  loaders migrate.
- No edited code runs in Update, FixedUpdate, ECS Run, render loops, or other
  hot paths.
- Use `dotnet build` and `dotnet test` with `-m:1 -nr:false`.

---

## Purpose / Big Picture

The template's MSDF font metadata and GLSL shaders become normal content
assets. Opening the game in the Editor creates missing sidecars; a command-line
build validates them, produces a manifest and content-addressed cooked files,
and places that output in each runtime bundle. A game created from a packed SDK
does the same without source paths to this repository.

## Progress

- [x] (2026-09-13) Approved ADR updated in commits `94d10d1` and `c5b55bb`.
- [x] (2026-09-13) Add font and shader processors with Core tests. Verified:
  `dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj --no-restore -m:1 -nr:false`
  passed 70 tests.
- [x] (2026-09-13) Package the CLI and analyzer and make SDK targets
  package-relative; package layout is asserted from the produced `.nupkg`.
- [x] (2026-09-13) Enable the template and add stable sidecars.
- [x] (2026-09-14) Enable the shared game Content root for both Client and
  Server; each cooks into its own intermediate output and runtime bundle.
- [x] (2026-09-30) Validate the packed external-template content workflow: a package-only ContentRefs.All consumer compiles; freshly materialized Static Client/Server launchers each build and publish a manifest plus eight cooked artifacts. Validation logs are under `artifacts/validation/static-final4-*` and `owned-sdk-content.trx`.
- [x] (2026-09-13) Update user-facing documentation.

## Surprises & Discoveries

- Observation: `Karpik.Content.Core.ContentBuildCoordinator.ScanAndValidate`
  requires a `.meta` for every source file under its source root.
  Evidence: `Karpik.Content.Core/ContentBuildCoordinator.cs:291-598`.
- Observation: `Karpik.Engine.Sdk/Sdk/Sdk.props` currently resolves the content
  CLI and code generator through engine-checkout paths.
  Evidence: `Karpik.Engine.Sdk/Sdk/Sdk.props:25-48`.
- Observation: the template contains a `.font-json`, `.vert`, and two `.frag`
  files, as well as JSON and image files.
  Evidence: `templates/Karpik.Game/Content/`.
- Observation: removing every extension gives identical logical names to
  `PressStart.font-json`/`PressStart.png` and `2D.vert`/`2D.frag`.
  Evidence: focused shared-template and CLI tests.
- Observation: package globs must be added immediately before NuGet gathers
  package files; evaluation-time globs see no project-reference output in a
  clean pack.
  Evidence: initial package omitted the payload until
  `_KarpikPackageContentPayload` was added before `_GetPackageFiles`.
- Observation: an MSBuild `Exec` argument ending in `\` escapes its closing
  quote on Windows. Content output defaults must not carry a trailing slash.
  Evidence: external template build passed `--output "...\\Content\"` and
  the CLI reported missing arguments.
- Observation: Roslyn resolves a relative `KarpikContentManifest` from its own
  working directory, not from the game project.
  Evidence: the external template wrote both manifests but emitted KCO301 for
  `manifest.json` until the SDK made the compiler-visible path absolute.

## Decision Log

- Decision: publish source bytes for fonts and shaders instead of a compiled
  backend format.
  Rationale: processor behavior stays deterministic and headless; GLSL
  compilation belongs to a future graphics-backend runtime slice.
  Date/Author: 2026-09-13 / developer and AI assistant.
- Decision: package the CLI and analyzer in `Karpik.Engine.Sdk`.
  Rationale: the generated game is an external NuGet consumer and cannot
  resolve repository-relative projects.
  Date/Author: 2026-09-13 / developer and AI assistant.
- Decision: copy cooked files into `TargetDir/Content` while retaining source
  files.
  Rationale: manifest consumers can access cooked assets without breaking the
  legacy path-based loaders that still require source names.
  Date/Author: 2026-09-13 / developer and AI assistant.
- Decision: map `.font-json` to the logical suffix `.font` and retain `.vert`
  or `.frag` for shaders.
  Rationale: one source basename may legitimately have both image/font or
  vertex/fragment files; logical names must remain unique.
  Date/Author: 2026-09-13 / developer and AI assistant.
- Decision: both template runtime projects cook the common game `Content/`
  tree independently.
  Rationale: each runtime bundle stays self-contained; shared prefab schemas
  can live in Shared without creating a Client-to-Server project reference.
  Date/Author: 2026-09-14 / developer and AI assistant.

## Outcomes & Retrospective

No implementation outcome yet.

## Context and Orientation

`IContentProcessor` is the build-time extension point. The default
`ContentBuildCoordinator` selects a processor by `AssetMeta.DeclaredType`,
calls `Process`, then publishes `manifest.json` and content-addressed cooked
files under the requested output directory. `RawJsonProcessor` is an existing
example of semantic validation; `TextureProcessor` is an existing example of
headless source-byte cooking.

`ContentMetaTemplate.TryCreate(relativePath, namespace, out metaJson)` is the
shared mapping used by the Editor and `content create`, so extension mappings
belong there, not in either front end. `ProjectViewModel.EnsureMetaFiles`
already calls this helper and preserves existing sidecars.

`Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj` determines the NuGet layout.
`Sdk/Sdk.props` adds the analyzer and invokes content build. `Sdk/Sdk.targets`
builds a runtime bundle after `CopyFilesToOutputDirectory`; a new target in
`Sdk.props` can run before `BuildKarpikRuntimeBundle` and copy the cooked
output into `TargetDir/Content`.

The template's Client and Server projects are content-build owners:
`templates/Karpik.Game/Source/KarpikGame.Client/KarpikGame.Client.csproj` and
`templates/Karpik.Game/Source/KarpikGame.Server/KarpikGame.Server.csproj`.
Their source root must be `$(MSBuildProjectDirectory)\..\..\Content`.

## Real-Time Assessment

The work occurs only during design-time editor scans and MSBuild execution.
It changes no ECS, tick, render, or runtime loading code. Build-time arrays,
JSON parsing, image decoding, and file copies are permitted. The package
layout keeps `Content.Core` independent of Client, Server, and graphics
projects; no locks, shared buffers, or runtime concurrency are introduced.

## Plan of Work

First make each new source format independently validatable and auto-mappable.
Then make the package self-contained and teach SDK targets to consume its
files. Last, opt the template into that SDK behavior, generate its sidecars,
and verify an external game build emits cooked content into the bundle.

## Milestones

1. Core accepts font metadata and shader source with targeted negative tests.
2. An SDK package contains every CLI/analyzer file required by an external
   game, and its targets have no checkout dependency for content.
3. The template builds content from its game `Content` root and ships cooked
   output in both runtime bundles.

## Concrete Steps

All commands run from `C:\Users\artem\RiderProjects\KarpikEngine`.

### Task 1: Add headless font and shader processors

**Files:**
- Modify: `Karpik.Content.Core/AssetMeta.cs`
- Modify: `Karpik.Content.Core/ContentBuildCoordinator.cs`
- Modify: `Karpik.Content.Core/ContentDiagnostic.cs`
- Modify: `Karpik.Content.Core/ContentMetaTemplate.cs`
- Create: `Karpik.Content.Core/FontJsonProcessor.cs`
- Create: `Karpik.Content.Core/ShaderProcessor.cs`
- Create: `Karpik.Content.Tests/FontJsonProcessorTests.cs`
- Create: `Karpik.Content.Tests/ShaderProcessorTests.cs`
- Modify: `Karpik.Content.Tests/ContentMetaTemplateTests.cs`
- Modify: `Karpik.Content.Tests/TextureContentBuildTests.cs`

**Interfaces:**
- Consumes: `IContentProcessor.Process(ReadOnlySpan<byte>, AssetMeta, string)`.
- Produces: `AssetMeta.ExpectedDeclaredTypeFontJson = "font-json"`,
  `AssetMeta.ExpectedDeclaredTypeShader = "shader"`, and processors registered
  by the default `ContentBuildCoordinator`.

- [ ] **Step 1: Write failing tests for mappings and processors.**

  Add theory cases asserting `.font-json -> font-json`, `.vert -> shader`, and
  `.frag -> shader` in `ContentMetaTemplateTests`. Add tests that:

  ```csharp
  Assert.True(processor.Process("{\"atlas\":{}}"u8, meta, "font.font-json").Diagnostics.Count == 0);
  Assert.Contains(processor.Process("not-json"u8, meta, "font.font-json").Diagnostics,
      diagnostic => diagnostic.Code == ContentDiagnosticCodes.InvalidJsonContent);
  Assert.Empty(shader.Process("#version 450\nvoid main() {}"u8, meta, "main.vert").Diagnostics);
  Assert.Contains(shader.Process([] , meta, "main.frag").Diagnostics,
      diagnostic => diagnostic.Code == ContentDiagnosticCodes.InvalidShaderContent);
  ```

  Extend the mixed-build test with one font JSON and one vertex shader, then
  assert manifest entries use `font-json` and `shader` and each cooked file
  has exactly the source bytes.

- [ ] **Step 2: Run the new tests to verify they fail.**

  Run:

  ```powershell
  dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~FontJsonProcessorTests|FullyQualifiedName~ShaderProcessorTests|FullyQualifiedName~ContentMetaTemplateTests"
  ```

  Expected: compilation failure because the new constants and processor types
  do not exist.

- [ ] **Step 3: Implement the smallest processor contract.**

  Add the two constants. `FontJsonProcessor` must parse the input with
  `JsonDocument.Parse(ReadOnlySpan<byte>)`, report `InvalidJsonContent` on
  `JsonException`, and return `sourceBytes.ToArray()` on success. It must have
  `DeclaredType == "font-json"` and `Version == "1.0.0"`.

  Add `ContentDiagnosticCodes.InvalidShaderContent = "KCO021"`.
  `ShaderProcessor` must allow only `.vert` and `.frag` by case-insensitive
  extension, reject an empty span and decoding failures from
  `new UTF8Encoding(false, true).GetString(sourceBytes)` with `KCO021`, and
  return `sourceBytes.ToArray()` with no dependencies on success. Its declared
  type is `shader`, and version is `1.0.0`.

  Register both processors in the default coordinator and extend
  `ContentMetaTemplate` with the three extension mappings. Do not refactor
  existing processors into a general hierarchy.

- [ ] **Step 4: Run the Core test project.**

  Run:

  ```powershell
  dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj --no-restore -m:1 -nr:false
  ```

  Expected: all content tests pass, including valid/invalid font and shader
  cases and a mixed manifest.

- [ ] **Step 5: Commit the Core slice.**

  ```powershell
  git add Karpik.Content.Core Karpik.Content.Tests
  git commit -m "Add font and shader content processors"
  ```

### Task 2: Package CLI and code generator with the SDK

**Files:**
- Modify: `Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj`
- Modify: `Karpik.Engine.Sdk/Sdk/Sdk.props`
- Modify: `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs`

**Interfaces:**
- Consumes: `content.dll` published by `Karpik.Content.Tool` and analyzer DLLs
  published by `Karpik.Content.Codegen`.
- Produces: package-relative `$(KarpikContentToolDll)` and an `Analyzer` item
  for `Karpik.Content.Codegen.dll`; neither references a repository project.

- [ ] **Step 1: Write the failing package-layout assertion.**

  In `ExternalGameCliTests`, extend the existing packed-SDK external workflow
  to inspect the created `.nupkg` and assert it contains:

  ```text
  tools/net10.0/content/content.dll
  tools/net10.0/content/content.runtimeconfig.json
  analyzers/dotnet/cs/Karpik.Content.Codegen.dll
  ```

  Add assertions that the generated game build log has no
  `KarpikRepositoryRoot`, `Karpik.Content.Tool.csproj`, or missing-analyzer
  diagnostic.

- [ ] **Step 2: Run the targeted test to verify it fails.**

  Run:

  ```powershell
  $env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION='1'
  dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~ExternalGameCliTests"
  ```

  Expected: the package assertions fail because the content CLI and analyzer
  are not included.

- [ ] **Step 3: Make the SDK package self-contained.**

  In `Karpik.Engine.Sdk.csproj`, add build-only project references to
  `Karpik.Content.Tool` and `Karpik.Content.Codegen`. Define output-path
  properties for their configuration-specific `bin` directories. Pack every
  file from the tool output directory to `tools/net10.0/content/` and every
  file from the codegen output directory to `analyzers/dotnet/cs/`; this keeps
  `.deps.json`, `.runtimeconfig.json`, StbImageSharp, and analyzer dependencies
  beside their entry assemblies.

  In `Sdk.props`, set the default `KarpikContentToolDll` to the packaged
  `tools/net10.0/content/content.dll`. Replace the checkout `ProjectReference`
  with an `Analyzer Include` pointing at the packaged
  `analyzers/dotnet/cs/Karpik.Content.Codegen.dll`. Remove
  `KarpikContentToolBuild`; `KarpikContentBuild` must invoke only an existing
  packaged DLL.

- [ ] **Step 4: Copy cooked output into the runtime bundle input.**

  Add `KarpikContentCopyToRuntimeOutput` in `Sdk.props` with:

  ```xml
  <Target Name="KarpikContentCopyToRuntimeOutput"
          BeforeTargets="BuildKarpikRuntimeBundle"
          DependsOnTargets="KarpikContentBuild"
          Condition="'$(KarpikProjectKind)' == 'Runtime' and ('$(KarpikSide)' == 'Client' or '$(KarpikSide)' == 'Server') and Exists('$(KarpikContentManifest)')">
    <ItemGroup>
      <_KarpikCookedContent Include="$(KarpikContentOutput)**\*" />
    </ItemGroup>
    <Copy SourceFiles="@(_KarpikCookedContent)"
          DestinationFiles="@(_KarpikCookedContent-&gt;'$(TargetDir)Content\%(RecursiveDir)%(Filename)%(Extension)')"
          SkipUnchangedFiles="true" />
  </Target>
  ```

  Do not remove the template's existing `Content` items; they keep legacy
  loaders working.

- [ ] **Step 5: Re-run the packed-SDK external workflow.**

  Run the command from Step 2. Expected: it completes with a package-native
  content CLI and analyzer; the test's generated external game remains within
  its temporary root.

- [ ] **Step 6: Commit the package slice.**

  ```powershell
  git add Karpik.Engine.Sdk Karpik.Engine.Sdk.IntegrationTests
  git commit -m "Package content tools with the SDK"
  ```

### Task 3: Enable and seed the game template

**Files:**
- Modify: `templates/Karpik.Game/Source/KarpikGame.Client/KarpikGame.Client.csproj`
- Create: `templates/Karpik.Game/Content/Player.json.meta`
- Create: `templates/Karpik.Game/Content/PressStart.font-json.meta`
- Create: `templates/Karpik.Game/Content/PressStart.png.meta`
- Create: `templates/Karpik.Game/Content/Sprites/Player.png.meta`
- Create: `templates/Karpik.Game/Content/Sprites/default.jpg.meta`
- Create: `templates/Karpik.Game/Content/Shaders/2D.vert.meta`
- Create: `templates/Karpik.Game/Content/Shaders/2D.frag.meta`
- Create: `templates/Karpik.Game/Content/Shaders/TextSdf.frag.meta`
- Modify: `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs`
- Modify: `docs/content-pipeline.md`

**Interfaces:**
- Consumes: the packaged SDK target properties and the shared
  `ContentMetaTemplate` extension mappings.
- Produces: a Client project with `KarpikContentEnabled=true`, namespace
  `game`, and source root at the game-owned Content directory.

- [ ] **Step 1: Write failing template assertions.**

  Extend `Template_has_the_standard_external_game_structure` to require the
  eight `.meta` files. Add a focused template-project assertion that reads
  `KarpikGame.Client.csproj` and requires:

  ```xml
  <KarpikContentEnabled>true</KarpikContentEnabled>
  <KarpikContentNamespace>game</KarpikContentNamespace>
  <KarpikContentSourceRoot>$(MSBuildProjectDirectory)\..\..\Content</KarpikContentSourceRoot>
  ```

  In the external CLI workflow, build the Client runtime project and assert
  both `bin/.../Content/manifest.json` and
  `bin/.../karpik-bundle/Content/manifest.json` exist, with at least one
  `artifacts/**/*.cooked` file in each location.

- [ ] **Step 2: Run the targeted template test to verify it fails.**

  Run the command from Task 2, Step 2. Expected: missing sidecars and Client
  content properties cause the new assertions to fail.

- [ ] **Step 3: Enable the Client content build and create stable sidecars.**

  Add the three asserted `KarpikContent*` properties to the Client project's
  existing property group. After Task 1 is green, create the eight sidecars
  through the shared CLI contract, preserving each generated ID in git:

  ```powershell
  $templateContent = 'templates\Karpik.Game\Content'
  dotnet run --project Karpik.Content.Tool -- create --source $templateContent --file Player.json --namespace game
  dotnet run --project Karpik.Content.Tool -- create --source $templateContent --file PressStart.font-json --namespace game
  dotnet run --project Karpik.Content.Tool -- create --source $templateContent --file PressStart.png --namespace game
  dotnet run --project Karpik.Content.Tool -- create --source $templateContent --file Sprites\Player.png --namespace game
  dotnet run --project Karpik.Content.Tool -- create --source $templateContent --file Sprites\default.jpg --namespace game
  dotnet run --project Karpik.Content.Tool -- create --source $templateContent --file Shaders\2D.vert --namespace game
  dotnet run --project Karpik.Content.Tool -- create --source $templateContent --file Shaders\2D.frag --namespace game
  dotnet run --project Karpik.Content.Tool -- create --source $templateContent --file Shaders\TextSdf.frag --namespace game
  ```

  Verify generated logical names begin with `game/` and declared types match
  the mapping. Never regenerate an existing sidecar during a retry.

- [ ] **Step 4: Update the user-facing content documentation.**

  In `docs/content-pipeline.md`, list `.font-json`, `.vert`, and `.frag` as
  supported extensions, state that the packaged SDK supports external games,
  and replace the manual cooked-copy target with the SDK's automatic runtime
  bundle behavior. Retain the note that runtime font/shader/texture loaders are
  future work and that sources remain in the bundle for legacy loaders.

- [ ] **Step 5: Run acceptance validation.**

  Run:

  ```powershell
  dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj --no-restore -m:1 -nr:false
  dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj --no-restore -m:1 -nr:false
  $env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION='1'
  dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~ExternalGameCliTests"
  git diff --check
  ```

  Expected: all selected tests pass; the external game gets package-native
  content generation and cooked content in `bin` and `karpik-bundle`.

- [ ] **Step 6: Commit the template and documentation slice.**

  ```powershell
  git add templates/Karpik.Game Karpik.Engine.Sdk.IntegrationTests docs/content-pipeline.md plans/packaged-content-formats-execplan.md
  git commit -m "Enable packaged content pipeline in game template"
  ```

## Validation and Acceptance

Acceptance requires all of the following observable results:

- `content create` and Editor meta generation map font and shader extensions to
  the declared types in the ADR.
- Invalid font JSON and empty or invalid UTF-8 shaders fail content build with
  stable diagnostics; valid input publishes unchanged bytes.
- The packed SDK's `tools/net10.0/content/` directory contains a runnable
  `content.dll` payload and `analyzers/dotnet/cs/` contains the code generator.
- A new external template game uses only its NuGet package to build the Client
  project, emits a manifest and artifacts, and contains both under its runtime
  bundle's `Content` directory.
- The template's original named source files are still present for legacy
  loaders.

## Idempotence and Recovery

Processor tests and package builds are safe to rerun. The external integration
test owns a generated temporary directory and cleans it in its existing
finally block. CLI `create` intentionally fails rather than overwrites an
existing sidecar; on a retry, inspect the tracked `.meta` and retain its
`assetId`. If package contents or bundle copy validation fails, revert only the
current uncommitted task slice and keep the approved ADR commits intact.

## Artifacts and Notes

- Durable format and delivery decision:
  `docs/02_ADR/content-pipeline-texture-source-format.md`.
- Existing SDK external-game harness:
  `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs`.
- This change is build-time only; runtime loaders and GPU shader compilation
  are deliberately excluded.
