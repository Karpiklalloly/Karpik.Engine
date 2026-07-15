# Deliver a versioned SDK and external game-project workflow

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

After this plan is complete, KarpikEngine source, installed engine versions, and games are separate ownership units. A developer can create a game in any directory, keep only standard `.slnx`, `global.json`, and SDK-style `.csproj` files in that game, and run ordinary `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet publish` commands without opening the editor. Every project in the game solution uses `Karpik.Engine.Sdk`, declares its project kind and side, and is checked against the same module and Client/Server/Shared rules in CLI, Rider, CI, and the editor.

The Karpik launcher resolves the exact SDK version pinned by the game, starts the compatible versioned editor, and keeps a recent-project list. An editor owns one active project, but that project may run one server and multiple clients. Installed engine payloads and source-built development payloads have the same layout; games never commit relative references to the engine source checkout.

The durable decision is recorded in `docs/02_ADR/versioned-engine-sdk-and-external-game-projects.md`. The delivery overview is mirrored in `docs/04_Roadmap/kanban-versioned-sdk-external-projects.md`.

## Progress

- [x] (2026-07-15) Architecture agreed and accepted ADR committed as `b68f055`.
- [x] (2026-07-15) Initial ExecPlan and kanban board created.
- [x] (2026-07-15) Milestone 1: reusable game-solution model and validator complete (`5765d51`, `cdc613f`; review clean; ProjectModel 30/30, Configurator 9/9, module validation passed).
- [ ] Milestone 2: `Karpik.Engine.Sdk` can be packed and resolve through standard MSBuild SDK resolution.
- [ ] Milestone 3: transactional engine payload packager and resolver complete.
- [ ] Milestone 4: an external fixture game builds with ordinary `dotnet` commands.
- [ ] Milestone 5: project-owned client/server bundles run through engine-owned runners.
- [ ] Milestone 6: the editor opens, closes, and switches one active external project safely.
- [ ] Milestone 7: the launcher selects a version-compatible editor and handles cross-version handoff.
- [ ] Milestone 8: monorepository game assumptions are removed and full acceptance passes.

## Surprises & Discoveries

- Observation: The existing Configurator is already a useful source of module graph rules, but `Configurator/RepositoryParser.cs` hard-codes both `Modules/` and four `MyGame/` paths and reads the module profile from the engine root `Directory.Build.props`.
  Evidence: `RepositoryParser.GameRootPaths`, `RepositoryParser.Load`, and `GraphValidator.ValidateConventions`.

- Observation: The current editor packages client and server runtime trees into `Karpik.Editor/bin/.../runtimes` by building repository-local `ClientLauncher` and `ServerLauncher` projects and copying `MyGame` content.
  Evidence: `Karpik.Editor/EditorRuntimeBundles.targets`.

- Observation: The current editor model persists a directory path and accepts any existing directory; it does not evaluate `.slnx`, `global.json`, project SDK identity, kind, side, or module graph.
  Evidence: `Karpik.Editor/Models/EditorWorkspace.cs` and `EditorShellViewModel.OpenProject`.

- Observation: Adding any project to `KarpikEngine.slnx` intentionally adds it to `Generated/KarpikModuleCatalog.props`; the catalog is a solution-wide id/path map, not only a runtime module list.
  Evidence: `Configurator/ArtifactGenerator.cs:BuildCatalog` and the accepted Milestone 1 generation diff.

- Observation: Raw XML safety requires location-aware parsing even without MSBuild evaluation; scanning arbitrary descendants can mistake target-time content for static declarations.
  Evidence: Milestone 1 review and regression tests in `Karpik.Engine.ProjectModel.Tests/GameSolutionValidationTests.cs`.

## Decision Log

- Decision: Use a thin NuGet-distributed custom MSBuild SDK plus a separate versioned engine payload.
  Rationale: MSBuild SDK resolution keeps games compatible with ordinary `dotnet` commands, while the payload can carry runners, native libraries, editor binaries, and modules that do not fit a managed package-only model.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Require `Karpik.Engine.Sdk` on every project in a game `.slnx` and require orthogonal `KarpikProjectKind` and `KarpikSide` properties.
  Rationale: Tests, tools, generators, assets, and runtime projects need different build profiles but must not bypass analyzers or side-boundary validation.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Do not introduce a `.karpik` manifest or require a game-level `Directory.Build.props` contract.
  Rationale: `.slnx`, `global.json`, and SDK-based `.csproj` files already provide a standard .NET project model and avoid duplicated state.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Use a stable launcher with an editor packaged per compatible engine installation.
  Rationale: The current editor directly references engine contracts; version-matched editors avoid immediate compatibility branches across historical APIs.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: Preserve one active project per editor and one server plus multiple clients within that project.
  Rationale: This isolates workers, IPC, ports, watchers, logs, hot-reload state, and build state while preserving the existing multisession editor workflow.
  Date/Author: 2026-07-15 / developer and Codex

- Decision: New tooling projects added to `KarpikEngine.slnx` receive normal deterministic entries in `Generated/KarpikModuleCatalog.props`.
  Rationale: Configurator intentionally catalogs every solution project. Excluding the new projects would require a special case and contradict that existing contract. `AutoGenerated.targets` and `Generated/ModuleLoader.cs` remain unchanged in Milestone 1.
  Date/Author: 2026-07-15 / developer and Codex

## Outcomes & Retrospective

No implementation outcome yet. At each milestone, record commands, observed results, deviations, and any follow-up work here. At completion, compare the external-game workflow with the accepted ADR and link any superseding decision.

## Context and Orientation

`KarpikEngine.slnx` currently contains engine libraries, first-party modules, editor projects, engine runners, Configurator, repository-local launchers, and `MyGame`. `Directory.Build.props`, `Directory.Build.targets`, `AutoGenerated.targets`, `Plugins.targets`, and `Generated/KarpikModuleCatalog.props` cooperate to turn shorthand `KarpikModuleDependency` items into project references and runtime plugin lists.

`Configurator/RepositoryParser.cs` parses the engine solution and raw project XML. `Configurator/GraphValidator.cs` validates module selection, dependency activation, cycles, and side boundaries. `Configurator/ArtifactGenerator.cs` generates repository-level build artifacts. These engine-repository responsibilities must remain available, but the reusable game-solution vocabulary and side rules must move into tooling that can ship with `Karpik.Engine.Sdk`.

`Karpik.Editor/EditorRuntimeBundles.targets` currently builds `ClientLauncher/ClientLauncher.csproj` and `ServerLauncher/ServerLauncher.csproj`, then copies their outputs and `MyGame/MyGameResources` below the editor output. `Karpik.Editor/Runtime/RuntimeBundleResolver.cs` consequently resolves bundles relative to `AppContext.BaseDirectory`. This is the primary ownership inversion to remove: engine runners belong to an engine installation, while game assemblies and content bundles belong to game build output.

`Karpik.Editor/ViewModels/EditorShellViewModel.cs` currently stores one folder in `ProjectPath`, owns one long-lived `EditorSessionManager`, and constructs backends from editor-local bundles. The new project context must instead own its build inspection, resolved installation, runtime bundles, watchers, and session manager. Switching projects destroys the complete old context before publishing the new one.

In this plan, an **MSBuild SDK** is a NuGet package containing `Sdk/Sdk.props` and `Sdk/Sdk.targets` that MSBuild resolves before evaluating a project. An **engine payload** is an installed directory containing runtime assemblies, runners, modules, native libraries, a compatible editor, and an installation manifest. A **game bundle** is side-specific output produced by a game project and loaded by an engine-owned runner.

## Planned File Structure and Interfaces

Create these focused units rather than extending Configurator or `EditorShellViewModel` into additional monoliths:

- `Karpik.Engine.ProjectModel/`: raw `.slnx` and `.csproj` parsing, project kind/side vocabulary, diagnostics, and game graph validation. It has no Avalonia, runtime, or MSBuild task dependency.
- `Karpik.Engine.ProjectModel.Tests/`: temporary-solution unit tests for parsing, SDK participation, side rules, cycles, and project kinds.
- `Karpik.Engine.Sdk.Tasks/`: MSBuild task adapters over `Karpik.Engine.ProjectModel` and bundle-publication tasks. It references `Microsoft.Build.Framework` and `Microsoft.Build.Utilities.Core` with runtime assets kept inside the SDK package.
- `Karpik.Engine.Sdk.Tasks.Tests/`: task-level tests with fake build engines and temporary project graphs.
- `Karpik.Engine.Sdk/`: pack-only project containing `Sdk/Sdk.props`, `Sdk/Sdk.targets`, package metadata, and the task assemblies.
- `Karpik.Engine.Tooling/`: installation manifests, `global.json` SDK-version parsing, engine-root resolution, atomic directory publication, and launcher/editor handoff records.
- `Karpik.Engine.Tooling.Tests/`: resolver, manifest, hash, and atomic-publication tests.
- `Karpik.Engine.Packager/`: CLI that builds or accepts prepared artifacts and publishes an engine payload.
- `Karpik.Engine.Packager.Tests/`: layout validation and failure-recovery tests.
- `Karpik.Engine.Sdk.IntegrationTests/`: opt-in end-to-end tests that pack the SDK into a local feed, materialize a game below the OS temporary directory, and invoke real `dotnet` subprocesses.
- `templates/Karpik.Game/`: `dotnet new` template containing `global.json`, `.slnx`, and Client/Server/Shared/Test projects that all use `Karpik.Engine.Sdk`.
- `Karpik.Editor/Projects/`: active-project context, MSBuild inspection, opening, switching, and teardown coordination.
- `Karpik.Launcher/` and `Karpik.Launcher.Tests/`: stable launcher UI and orchestration tests.

The public tooling contracts start as:

```csharp
public enum KarpikProjectKind { Runtime, Test, Tool, Generator, Assets }
public enum KarpikProjectSide { Client, Server, Shared, None }

public sealed record KarpikDiagnostic(string Code, string ProjectPath, string Message);
public sealed record KarpikModuleReference(string Id, string? Implementation, bool Optional);
public sealed record KarpikProjectDescriptor(
    string ProjectPath,
    IReadOnlyList<string> SdkNames,
    KarpikProjectKind Kind,
    KarpikProjectSide Side,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<KarpikModuleReference> Modules);

public sealed record KarpikSolutionModel(
    string SolutionPath,
    string SdkVersion,
    IReadOnlyList<KarpikProjectDescriptor> Projects);

public sealed class KarpikSolutionReader
{
    public KarpikSolutionModel Read(string solutionPath);
}

public sealed class KarpikSolutionValidator
{
    public IReadOnlyList<KarpikDiagnostic> Validate(KarpikSolutionModel model);
}
```

The installation and editor contracts start as:

```csharp
public sealed record EngineInstallationManifest(
    string EngineVersion,
    string MsBuildSdkVersion,
    string EditorVersion,
    int RuntimeProtocolVersion,
    int LayoutVersion,
    string ContentHash);

public sealed record EngineInstallation(string RootPath, EngineInstallationManifest Manifest);

public sealed class EngineInstallationResolver
{
    public EngineInstallation Resolve(string sdkVersion, string? overrideRoot = null);
}

public sealed record ProjectRuntimeDescriptor(
    KarpikProjectSide Side,
    string RunnerExecutablePath,
    string GameBundlePath);

public sealed class ActiveProjectContext : IAsyncDisposable
{
    public string SolutionPath { get; }
    public EngineInstallation Installation { get; }
    public IReadOnlyDictionary<KarpikProjectSide, ProjectRuntimeDescriptor> Runtimes { get; }
    public EditorSessionManager Sessions { get; }
    public ValueTask DisposeAsync();
}
```

Names may change only when evidence from a milestone requires it; record the change in the Decision Log before later tasks consume the new name.

## Real-Time Assessment

This work is tooling, build, process orchestration, and editor lifecycle work. Solution parsing, XML/JSON allocation, filesystem enumeration, hashing, MSBuild evaluation, process waits, and locks are acceptable only before runtime startup or during explicit editor operations.

No validator, resolver, manifest check, launcher service, file watcher, or asset-cache scan may be called from `Update`, `FixedUpdate`, ECS `Run`, render loops, serialization loops, or network pumps. Runner argument parsing and bundle selection happen once at process startup. Existing editor snapshots remain bounded and run at their established safe points.

Client projects may reference Client and Shared projects; Server projects may reference Server and Shared projects; Shared projects may reference only Shared projects. `None` is valid for Tool, Generator, and Assets projects and must not gain implicit runtime-side access. Tests declare the side they exercise. Physics/gameplay tick behaviour, ECS data layout, and network delivery semantics are unchanged.

Project switching uses asynchronous cancellation and ordered process teardown, not blocking waits on the Avalonia UI thread. Atomic SDK and bundle publication may use filesystem moves and a short process-local lock because they run outside the game loop. Repeated publication and teardown must be idempotent.

## Plan of Work

Build the new boundary from the inside out. First extract a reusable project model and prove the side and SDK-participation rules independently of MSBuild. Package those rules behind a thin custom SDK and prove a minimal external solution can restore. Build the versioned engine payload and transactional resolver next, because both the game bundle targets and launcher depend on its layout.

Once SDK and payload resolution are stable, materialize the game template outside the repository and make its standard `dotnet` commands pass. Then move bundle ownership from the editor to the game's Client and Server build outputs and teach the engine runner to load an explicit bundle directory. Only after that contract works should the editor gain active-project contexts and safe switching.

Add the launcher after editor command-line opening and installation manifests are proven. Finish by removing `MyGame`, repository launchers, editor-local runtime bundling, and Configurator assumptions from the engine composition. Do not delete the old path until the external fixture covers equivalent server, multi-client, content, module, and hot-reload startup.

## Milestones

### Milestone 1: Reusable solution model and validation

**Files**

- Create `Karpik.Engine.ProjectModel/Karpik.Engine.ProjectModel.csproj` and focused files under `Model/`, `Parsing/`, and `Validation/`.
- Create `Karpik.Engine.ProjectModel.Tests/Karpik.Engine.ProjectModel.Tests.csproj` and `GameSolutionValidationTests.cs`.
- Modify `Configurator/Models.cs`, `Configurator/RepositoryParser.cs`, and `Configurator/GraphValidator.cs` only where shared side vocabulary can replace duplicate rules without changing current generated artifacts.
- Modify `Configurator.Tests/ConfiguratorTests.cs` to prove existing engine-repository validation is unchanged.
- Modify `KarpikEngine.slnx` to include the new projects.

Write tests first using temporary `.slnx` and `.csproj` files. Cover a valid Runtime Client → Runtime Shared edge; forbidden Client → Server, Server → Client, and Shared → Client/Server edges; missing `Karpik.Engine.Sdk`; missing or invalid kind/side; duplicate and missing solution projects; a project-reference cycle; Test with an explicit side; and Tool/Generator/Assets with `None`.

The reader must inspect raw project XML to prove SDK participation. It must not evaluate untrusted project targets merely to discover whether a project is valid. Normalize paths with `Path.GetFullPath`, compare Windows paths case-insensitively and Unix paths case-sensitively through one injected comparer, and return these stable codes from `Karpik.Engine.ProjectModel/Diagnostics/KarpikDiagnosticCodes.cs`:

- `KARPIK001`: project does not include `Karpik.Engine.Sdk`;
- `KARPIK002`: missing or invalid `KarpikProjectKind`;
- `KARPIK003`: missing or invalid `KarpikSide`;
- `KARPIK004`: solution project is missing, duplicated, unreadable, or outside the allowed solution root;
- `KARPIK005`: forbidden side dependency;
- `KARPIK006`: project-reference cycle;
- `KARPIK007`: unknown or ambiguous module id/implementation;
- `KARPIK008`: required module dependency is disabled or missing.

Validation from `C:\Users\artem\RiderProjects\KarpikEngine`:

    dotnet test Karpik.Engine.ProjectModel.Tests\Karpik.Engine.ProjectModel.Tests.csproj -m:1 -nr:false
    dotnet test Configurator.Tests\Configurator.Tests.csproj -m:1 -nr:false

Expected observation: both projects pass; `dotnet run --project Configurator\Configurator.csproj -- --generate` adds only the two new tooling projects to `Generated/KarpikModuleCatalog.props`; `AutoGenerated.targets` and `Generated/ModuleLoader.cs` remain byte-for-byte unchanged; a subsequent `--validate` passes.

Commit boundary: `feat: add reusable Karpik game project validation`.

### Milestone 2: Pack and resolve `Karpik.Engine.Sdk`

**Files**

- Create `Karpik.Engine.Sdk.Tasks/Karpik.Engine.Sdk.Tasks.csproj` with `ValidateKarpikSolutionTask.cs` and `ValidateKarpikProjectReferencesTask.cs`.
- Create `Karpik.Engine.Sdk.Tasks.Tests/Karpik.Engine.Sdk.Tasks.Tests.csproj` with fake-build-engine tests.
- Create `Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj`, `Sdk/Sdk.props`, `Sdk/Sdk.targets`, and `README.md`.
- Add `artifacts/nuget/` to `.gitignore` if the existing ignore rules do not already cover it.
- Add the projects to `KarpikEngine.slnx`.

`Sdk/Sdk.props` imports `Microsoft.NET.Sdk/Sdk/Sdk.props`, defines no implicit side, and requires consumers to set both `KarpikProjectKind` and `KarpikSide`. `Sdk/Sdk.targets` imports `Microsoft.NET.Sdk/Sdk/Sdk.targets`, registers the compiled task assembly, runs solution validation before `PrepareForBuild` when `$(SolutionPath)` is present, and runs transitive project-reference validation for direct project builds. The target must not shell out to Configurator.

Pack the local development package as `Karpik.Engine.Sdk` version `0.6.0-local` into `artifacts/nuget`. Create a temporary smoke solution with this `global.json` fragment:

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestPatch" },
  "msbuild-sdks": { "Karpik.Engine.Sdk": "0.6.0-local" }
}
```

Use a temporary `NuGet.Config` that adds only the local feed plus the normal configured sources; do not modify the user's global NuGet configuration in tests. Verify a project containing `<Project Sdk="Karpik.Engine.Sdk">` restores and builds, and verify a foreign project in the `.slnx` fails with `KARPIK001` before compilation.

Validation:

    dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false
    dotnet pack Karpik.Engine.Sdk\Karpik.Engine.Sdk.csproj -m:1 -nr:false -p:PackageVersion=0.6.0-local -o artifacts\nuget

Expected observation: the `.nupkg` contains `Sdk/Sdk.props`, `Sdk/Sdk.targets`, and the task/runtime dependency assemblies exactly once; the smoke project builds with plain `dotnet build`.

Commit boundary: `feat: package Karpik custom MSBuild SDK`.

### Milestone 3: Transactional engine payload

**Files**

- Create `Karpik.Engine.Tooling/Karpik.Engine.Tooling.csproj` with `EngineInstallationManifest.cs`, `GlobalJsonSdkVersionReader.cs`, `EngineInstallationResolver.cs`, `EngineInstallationValidator.cs`, and `AtomicDirectoryPublisher.cs`.
- Create `Karpik.Engine.Tooling.Tests/Karpik.Engine.Tooling.Tests.csproj` with resolver and recovery tests.
- Create `Karpik.Engine.Packager/Karpik.Engine.Packager.csproj`, `Program.cs`, `EnginePayloadBuilder.cs`, and `PayloadLayout.cs`.
- Create `Karpik.Engine.Packager.Tests/Karpik.Engine.Packager.Tests.csproj`.
- Add the projects to `KarpikEngine.slnx`.

The manifest JSON fields are exactly `engineVersion`, `msBuildSdkVersion`, `editorVersion`, `runtimeProtocolVersion`, `layoutVersion`, and `contentHash`. The initial `layoutVersion` and `runtimeProtocolVersion` are `1`. The packager accepts explicit `--source`, `--output`, `--engine-version`, and `--sdk-version` arguments, writes to `<output>/.staging/<guid>`, validates all required files, computes a deterministic SHA-256 content hash over sorted normalized relative paths and file bytes, writes `.complete`, and atomically renames the staging directory to `<output>/Engines/<engine-version-or-dev-hash>`.

The payload layout is:

    editor/
    sdk/
    runners/client/
    runners/server/
    modules/
    native/
    engine-installation.json
    .complete

Tests cover wrong manifest version, a missing runner, a missing completion marker, hash mismatch, interrupted replacement, an existing good destination, explicit `KarpikEngineRoot`, and default resolution below `%LocalAppData%/Karpik/Engines` (or the platform-equivalent local application-data root).

Validation:

    dotnet test Karpik.Engine.Tooling.Tests\Karpik.Engine.Tooling.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Packager.Tests\Karpik.Engine.Packager.Tests.csproj -m:1 -nr:false
    dotnet run --project Karpik.Engine.Packager\Karpik.Engine.Packager.csproj -- --source . --output artifacts\karpik-home --engine-version 0.6.0-dev --sdk-version 0.6.0-local

Expected observation: only a complete, hash-valid payload appears below `artifacts/karpik-home/Engines`; rerunning the command either reuses the identical content-addressed result or replaces it atomically without a partially visible installation.

Commit boundary: `feat: add transactional engine SDK payload packaging`.

### Milestone 4: External game template and ordinary `dotnet` workflow

**Files**

- Create `templates/Karpik.Game/.template.config/template.json`.
- Create template `global.json`, `KarpikGame.slnx`, and projects under `Source/KarpikGame.Client`, `Source/KarpikGame.Server`, `Source/KarpikGame.Shared`, and `Tests/KarpikGame.Tests`.
- Create `Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj` and `ExternalGameCliTests.cs`.
- Add only the integration-test project, not the generated game, to `KarpikEngine.slnx`.

Every template `.csproj` uses `Karpik.Engine.Sdk`. Runtime projects declare `KarpikProjectKind=Runtime` and Client/Server/Shared respectively. Tests declare `KarpikProjectKind=Test` and the side they exercise. The template contains no `.karpik`, `Directory.Build.props`, `Directory.Build.targets`, or relative reference to KarpikEngine source.

The opt-in integration test creates a unique directory below `Path.GetTempPath()`, verifies that directory is not below the repository root, installs the template there, points NuGet at `artifacts/nuget`, sets `KarpikEngineRoot` to the payload from Milestone 3, and runs real subprocesses with bounded timeouts and captured output:

    dotnet restore KarpikGame.slnx
    dotnet build KarpikGame.slnx -m:1 -nr:false --no-restore
    dotnet test KarpikGame.slnx -m:1 -nr:false --no-build
    dotnet publish Source\KarpikGame.Client\KarpikGame.Client.csproj -m:1 -nr:false --no-restore

It then mutates a copy so one project uses `Microsoft.NET.Sdk` and proves solution build fails with `KARPIK001`. A second mutation creates a Client → Server reference and proves failure with the side-boundary diagnostic.

Validation:

    $env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION='1'
    dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false
    Remove-Item Env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION

Expected observation: all four standard `dotnet` operations pass outside the repository and both invalid copies fail before runtime startup.

Commit boundary: `feat: add external Karpik game template`.

### Milestone 5: Game-owned bundles and engine-owned runners

**Files**

- Add `BuildKarpikRuntimeBundleTask.cs` and tests to `Karpik.Engine.Sdk.Tasks/` and `.Tests/`.
- Modify `Karpik.Engine.Sdk/Sdk/Sdk.props` and `Sdk/Sdk.targets` to expose `KarpikRuntimeBundlePath` and create bundles only for Runtime Client/Server projects.
- Create `Karpik.Engine.Core/ProcessManagement/RuntimeLaunchOptions.cs`.
- Modify `Karpik.Engine.Core/Editor/EditorPreviewController.cs` and `Karpik.Engine.Core/ProcessManagement/ProcessManager.cs` to accept runner executable and bundle working directory separately.
- Modify `Karpik.Engine.Core.Runner/Program.cs` and `Runner.cs` to require an explicit `--bundle <absolute-path>` argument and load modules/content from that directory.
- Extend `Karpik.Engine.Core.Runner.Tests/` with runner-argument and explicit-bundle tests.
- Extend `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs` with side-purity and process-start coverage.

For each Runtime Client/Server project, default `KarpikRuntimeBundlePath` to `$(TargetDir)karpik-bundle/`. Publish through a sibling staging directory, verify the side marker and completed module staging marker, then atomically replace the final bundle. Never copy the engine runner into the game bundle. The runner executable comes from the resolved engine installation and receives the game bundle as an explicit argument.

Tests prove the Client bundle has no Server assemblies, the Server bundle has no Client graphics/input/window assemblies, both include Shared game assemblies and required content, and a runner cannot accidentally fall back to `AppContext.BaseDirectory` when the bundle argument is missing.

Validation:

    dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
    $env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION='1'
    dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --filter RuntimeBundle
    Remove-Item Env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION

Expected observation: the engine runner starts an external game's bundle without any repository-relative path and the bundle trees are side-pure.

Commit boundary: `feat: move runtime bundle ownership to game builds`.

### Milestone 6: One active external project in the editor

**Files**

- Create `Karpik.Editor/Projects/ProjectOpenResult.cs`, `MsBuildProjectInspector.cs`, `ActiveProjectContext.cs`, `ProjectOpenService.cs`, and `ProjectSwitchCoordinator.cs`.
- Create matching tests under `Karpik.Editor.Tests/Projects/`.
- Replace `Karpik.Editor/Runtime/RuntimeBundleResolver.cs` with `ProjectRuntimeResolver.cs` and update its tests.
- Modify `Karpik.Editor/Runtime/EditorPreviewBackendFactory.cs` to consume `ProjectRuntimeDescriptor` instances.
- Modify `Karpik.Editor/ViewModels/EditorShellViewModel.cs`, `Models/EditorWorkspace.cs`, `MainWindow.axaml`, and `MainWindow.axaml.cs` for asynchronous `.slnx` open/switch and workspace persistence.
- Modify `Karpik.Editor/Program.cs` to accept `--solution <absolute-path>` and `--handoff <absolute-path>`.
- Remove the `EditorRuntimeBundles.targets` import from `Karpik.Editor/Karpik.Editor.csproj` only after the new tests and external runtime smoke pass.

`ProjectOpenService.OpenAsync` first performs raw safe validation, then invokes MSBuild in a child process for evaluated properties and items with a bounded timeout. It returns a candidate context without publishing it. `ProjectSwitchCoordinator.SwitchAsync` blocks new commands, cancels any active build, stops clients, stops the server, disposes IPC/watchers/services, saves the old workspace, disposes the old context, validates the candidate, and finally publishes it as active. Failure before publication leaves no active context.

Tests use fake contexts and backends to prove exact teardown order, no overlapping active contexts, cancellation, failed teardown preventing open, failed candidate leaving no active project, workspace path round-trip as `.slnx`, and stale snapshot/output rejection after switching. A real opt-in test switches between two temporary external games and verifies worker PIDs exit and bundle paths change.

Validation:

    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false
    dotnet build Karpik.Editor\Karpik.Editor.csproj -m:1 -nr:false
    $env:KARPIK_RUN_EDITOR_PROJECT_SWITCH_INTEGRATION='1'
    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --filter ExternalProjectSwitch
    Remove-Item Env:KARPIK_RUN_EDITOR_PROJECT_SWITCH_INTEGRATION

Expected observation: the editor can open either external game, run one server and multiple clients from that game's bundles, switch projects, and leave no process, IPC endpoint, watcher, or locked file from the old context.

Commit boundary: `feat: add transactional external project switching to editor`.

### Milestone 7: Stable launcher and version-matched editors

**Files**

- Create `Karpik.Launcher/Karpik.Launcher.csproj`, Avalonia app/window files, `Models/RecentProject.cs`, `Services/ProjectRegistry.cs`, `Services/EditorResolver.cs`, `Services/EditorProcessHost.cs`, and `ViewModels/LauncherViewModel.cs`.
- Create `Karpik.Launcher.Tests/Karpik.Launcher.Tests.csproj` with registry, resolution, and process-handoff tests.
- Add `EditorHandoffRequest.cs` and `EditorExitCodes.cs` to `Karpik.Engine.Tooling/`.
- Modify `Karpik.Editor/Projects/ProjectSwitchCoordinator.cs` to write a handoff request and return the dedicated exit code when the target project requires an incompatible editor.
- Modify `Karpik.Engine.Packager/` so every payload includes its compatible editor.
- Add launcher projects to `KarpikEngine.slnx`.

The launcher reads only `global.json` and installation manifests before selecting an editor. It stores recent `.slnx` paths below the platform local application-data directory, never inside a game. It starts the editor with a unique handoff file path and waits asynchronously. Exit code `20` means the editor wrote a validated handoff request for another solution; the launcher resolves the new version and loops. Other non-zero codes are surfaced as failures and do not trigger automatic retries.

Tests create two fake installations with distinct editor executables and manifests. They prove exact-version selection, missing/corrupt/incomplete installation errors, recent-project deduplication, normal exit, exit-code-20 handoff, malformed handoff rejection, and bounded restart loops.

Validation:

    dotnet test Karpik.Engine.Tooling.Tests\Karpik.Engine.Tooling.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj -m:1 -nr:false
    dotnet build Karpik.Launcher\Karpik.Launcher.csproj -m:1 -nr:false

Manual smoke: install two development payloads with different compatible editor versions, open one project through the launcher, request a switch to the second project, observe all old workers exit, and observe the launcher start the second editor with the requested `.slnx`.

Commit boundary: `feat: add version-aware Karpik project launcher`.

### Milestone 8: Remove monorepository game assumptions

**Files**

- Remove `Karpik.Editor/EditorRuntimeBundles.targets` after all references are gone.
- Remove `ClientLauncher/`, `ServerLauncher/`, and their solution entries after runner and bundle integration tests replace their composition-root role.
- Move reusable `MyGame` sample source/content into `templates/Karpik.Game/` where appropriate, then remove `MyGame/` and its solution entries.
- Remove `MyGame` and launcher entries from `Plugins.targets`, `Directory.Build.props`, `Directory.Build.targets`, `AutoGenerated.targets`, and generated catalog/loader output.
- Modify `Configurator/RepositoryParser.cs`, `Models.cs`, `GraphValidator.cs`, and `ArtifactGenerator.cs` so engine repository validation has no `MyGame` roots and emits an SDK payload module catalog instead of a game-specific graph.
- Update `Generated/KarpikModuleCatalog.props` and `Generated/ModuleLoader.cs` through Configurator, not by hand.
- Update `README.md`, `README-ENG.md`, `docs/02_ADR/editor-desktop-stack.md`, this ExecPlan, and the kanban board.

Before deleting old paths, prove the external template carries the required sample behaviour and content and that the current server-plus-two-clients smoke succeeds outside the repository. Keep first-party modules under `Modules/`; the separation requirement concerns games, game launchers, and game resources, not the engine's own module source.

Validation from the repository root:

    dotnet run --project Configurator\Configurator.csproj -- --generate
    dotnet run --project Configurator\Configurator.csproj -- --validate
    dotnet test Configurator.Tests\Configurator.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.ProjectModel.Tests\Karpik.Engine.ProjectModel.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Tooling.Tests\Karpik.Engine.Tooling.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Packager.Tests\Karpik.Engine.Packager.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj -m:1 -nr:false
    dotnet build KarpikEngine.slnx -m:1 -nr:false --no-restore
    git diff --check
    graphify update .

Then run both opt-in external SDK and editor switching suites and the manual cross-version launcher smoke described above.

Expected observation: no evaluated build, generated artifact, editor path, or runtime startup requires `MyGame`, `ClientLauncher`, `ServerLauncher`, or a game below the engine repository root. The engine solution, external game solution, and two installed payloads remain independently buildable.

Commit boundary: `refactor: separate engine source from game projects`.

## Concrete Steps

Work milestone by milestone. Before each milestone, update `Progress` with the intended start and verify `git status --short` so unrelated `.obsidian` changes remain untouched. Within a milestone, use the sequence: add the smallest failing unit/integration test, run it and record the expected failure, add the minimal production code, rerun the targeted test, run the milestone validation set, update this plan, and commit only the milestone files.

Use single-node .NET commands throughout:

    dotnet build <project-or-solution> -m:1 -nr:false
    dotnet test <project-or-solution> -m:1 -nr:false

When a real subprocess test needs a local SDK package, first recreate the local feed deterministically:

    dotnet pack Karpik.Engine.Sdk\Karpik.Engine.Sdk.csproj -m:1 -nr:false -p:PackageVersion=0.6.0-local -o artifacts\nuget

When a test needs an engine payload, publish it below repository-local ignored artifacts, not into the user's real installation store:

    dotnet run --project Karpik.Engine.Packager\Karpik.Engine.Packager.csproj -- --source . --output artifacts\karpik-home --engine-version 0.6.0-dev --sdk-version 0.6.0-local

Do not run deletion migrations in Milestone 8 until the Milestone 4 through 7 acceptance evidence is recorded in `Progress`.

## Validation and Acceptance

Automatic acceptance requires all milestone test projects and the engine solution build to pass with single-node MSBuild. The external integration test must create its game outside the repository and demonstrate successful restore, build, test, publish, client bundle construction, server bundle construction, and runtime start using only the local NuGet SDK feed and versioned engine payload.

Invalid solutions must fail with stable diagnostics when any project omits `Karpik.Engine.Sdk`, omits kind/side, violates side boundaries, contains a cycle, selects an unknown/disabled module, or references a missing project. Direct project builds must cover their full transitive project-reference graph.

User-visible acceptance requires:

- Launcher shows recent `.slnx` projects and selects the exact installed SDK/editor version from `global.json`.
- A game opens without a `.karpik` manifest or required `Directory.Build.props`.
- The same game builds through editor commands and ordinary terminal `dotnet` commands.
- A source-built payload override builds and runs the game without changing committed game files.
- One editor owns one active project, one server, and multiple clients.
- Same-version switching happens in-process after complete teardown; incompatible switching returns through the launcher and starts the matching editor.
- Failed SDK resolution, validation, build, bundle publication, runtime startup, or project switching leaves an explicit diagnostic and no partially active project.
- After migration, the engine repository contains no game-specific composition root or resource path.

Real-time acceptance requires code inspection and tests to confirm that all new work stops at build/startup/editor boundaries. There must be no new invocation from `Update`, `FixedUpdate`, ECS `Run`, render, serialization, or network pump paths.

## Idempotence and Recovery

SDK packing, payload publication, bundle publication, template materialization, project open, project teardown, and launcher handoff must be safe to retry. Staging directories include unique names and become visible only after validation and a completion marker. On replacement failure, restore the last complete destination and retain a diagnostic path to the failed staging directory only in test or verbose mode.

Tests use unique temporary directories and kill owned process trees in `finally` blocks. They never modify global NuGet sources or the real `%LocalAppData%/Karpik/Engines` store. A failed editor switch disposes the candidate and leaves no active project. A failed cross-version handoff does not restart indefinitely; the launcher shows the error and returns to project selection.

Milestones 1 through 7 are additive and can be rolled back by reverting their milestone commits. Milestone 8 deletes legacy paths only after replacement coverage passes. If Milestone 8 fails, revert only its migration commit and keep the additive SDK/payload/editor infrastructure; do not use `git reset --hard` and do not touch unrelated `.obsidian` files.

## Artifacts and Notes

- Accepted architecture: `docs/02_ADR/versioned-engine-sdk-and-external-game-projects.md`.
- Desktop editor decision: `docs/02_ADR/editor-desktop-stack.md`.
- Existing module graph decision: `docs/02_ADR/module-graph.md`.
- Delivery board: `docs/04_Roadmap/kanban-versioned-sdk-external-projects.md`.
- Existing editor baseline: `plans/editor-first-slice-execplan.md`.
- Existing multisession baseline: `plans/editor-multisession-launch-execplan.md`.
- Local, ignored package feed: `artifacts/nuget/`.
- Local, ignored payload store: `artifacts/karpik-home/`.
