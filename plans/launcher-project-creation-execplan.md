# Launcher Project Creation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the launcher create a named game from a template shipped with a selected installed SDK, without changing the user's global `dotnet new` registrations.

**Architecture:** Package the existing templates as a versioned `Karpik.Engine.Templates` NuGet package and write a payload-local catalog beside it. Extend the existing installation resolver with valid-payload enumeration. A launcher service validates that catalog, runs `dotnet new` in a private hive and sibling staging directory, and a focused Avalonia dialog gathers the five user selections.

**Tech Stack:** .NET 10, standard `dotnet new`, NuGet template packages, `System.Text.Json`, Avalonia 12, ReactiveUI, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-14-launcher-project-creation-design.md`

## Global Constraints

- Use the standard template engine; do not implement a custom copier or token replacement engine.
- Generated `global.json` must pin the selected installation's exact `MsBuildSdkVersion`.
- Do not mutate installed payloads or a global template hive.
- Create only into a sibling staging directory, then publish once with `Directory.Move`; never overwrite a target.
- Payloads without `sdk/templates.json` remain valid for opening existing projects but expose no creation templates.
- This is cold desktop/package tooling. Do not alter Client, Server, Shared, ECS, update loops, renderer, serialization, or network code.
- Every build/test command includes `-m:1 -nr:false`.

---

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

A developer will choose an installed SDK (newest preselected), one template from that SDK, a name, a parent folder, and whether to launch immediately. The launcher creates `<parent>/<name>`, validates its root solution and SDK pin, registers it in Recent projects, and only launches the editor if requested.

## Progress

- [x] (2026-09-14) Initial plan created.
- [x] (2026-09-14) Baseline: tooling tests passed 41/41; launcher tests passed 16/16 when Avalonia received access to its standard LocalAppData log.
- [x] (2026-09-14) Template package and payload catalog validated.
- [x] (2026-09-14) Valid SDK enumeration validated.
- [x] (2026-09-14) Transactional creation service validated.
- [x] (2026-09-14) Create-project dialog and optional editor launch validated.
- [x] (2026-09-14) Targeted tests and package-to-launcher smoke complete.

## Surprises & Discoveries

- Observation: `templates/Karpik.Game` already follows the `dotnet new` format but hardcodes `0.6.0-local` in `global.json`.
  Evidence: `templates/Karpik.Game/.template.config/template.json` and `templates/Karpik.Game/global.json`.
- Observation: the packager already hashes everything under the payload's `sdk/` directory.
  Evidence: `Karpik.Engine.Packager/PayloadLayout.cs:140-148` and `Karpik.Engine.Tooling/EngineInstallationValidator.cs:120-170`.
- Observation: `dotnet new install <local .nupkg>` and `--debug:custom-hive` are supported standard CLI features.
  Evidence: [Microsoft template packages](https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates) and [template parameters](https://learn.microsoft.com/en-us/dotnet/core/tools/templates).
- Observation: launcher test compilation writes Avalonia telemetry to `%LocalAppData%/AvaloniaUI/BuildServices/buildtasks.log`.
  Evidence: the sandbox-only baseline failed before compilation with `AvaloniaStatsTask` `UnauthorizedAccessException`; the identical elevated run passed 16/16.

## Decision Log

- Decision: Store UI metadata in `sdk/templates.json` and template content in normal `.nupkg` files.
  Rationale: the launcher does not parse localized `dotnet new list` output, while standard template processing remains available for future options.
  Date/Author: 2026-09-14 / user and Codex.
- Decision: Use a one-use custom hive plus sibling staging directory.
  Rationale: the exact selected SDK owns the template, and a failure cannot produce a visible partial project.
  Date/Author: 2026-09-14 / user and Codex.

## Outcomes & Retrospective

Delivered: `Karpik.Engine.Templates` is packed into each repository-built payload and described by `sdk/templates.json`; the launcher lists valid local SDK installations, defaults to the newest, and creates a selected template with a private `dotnet new` hive and staging directory. The dialog registers the generated solution and opens it only when requested. Verified with launcher tests (19/19), tooling tests (42/42), Packager build, package-content inspection, and a real `dotnet new` generation that pinned SDK version `1.2.3-test`. No ADR is expected: this is a packaging/launcher delivery detail, not a runtime architecture decision.

## Context and Orientation

`Karpik.Launcher/ViewModels/LauncherViewModel.cs` opens a selected `.slnx`, records it via `ProjectRegistry`, and launches the editor through `IEditorProcessHost`; `IsBusy` already prevents overlapping operations. `Karpik.Launcher/LauncherView.axaml` has the existing open button, status, and recent projects.

`Karpik.Engine.Tooling/EngineInstallationResolver.cs` owns validation and discovery under `%LocalAppData%/Karpik/Engines`, but resolves only one exact SDK today. `PayloadLayout.MaterializeRepository` packs `Karpik.Engine.Sdk` into the payload's `sdk/` directory. `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs` already asserts the current template structure. Extend these paths rather than creating parallel infrastructure.

## Real-Time Assessment

No edited code is a runtime hot path. The feature performs JSON parsing, file I/O, process execution, and optional editor startup only from an Avalonia command. There are no Client/Server/Shared boundary, allocation-budget, tick, ECS, serialization, networking, or rendering changes.

## Plan of Work

Implement in four independently verifiable layers: template package/catalog, installed-SDK enumeration, safe creation service, then the dialog and existing launcher integration. Each layer has a focused test cycle and commit.

## Milestones

1. A repository-built payload contains a version-matched template package and catalog.
2. Tooling lists only valid installations newest-first.
3. The launcher service validates input/catalog/output and publishes no partial targets.
4. The dialog selects SDK/template/name/directory and honors the open-now checkbox.

## Concrete Steps

### Task 1: Package the template and write a payload catalog

**Files:**

- Create: `Karpik.Engine.Templates/Karpik.Engine.Templates.csproj`
- Create: `templates/catalog.json`
- Create: `templates/Karpik.Game/.template.config/dotnetcli.host.json`
- Modify: `templates/Karpik.Game/.template.config/template.json`
- Modify: `templates/Karpik.Game/global.json`
- Modify: `Karpik.Engine.Packager/PayloadLayout.cs`
- Modify: `Karpik.Engine.Packager.Tests/EnginePayloadBuilderTests.cs`
- Modify: `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs`
- Modify: `KarpikEngine.slnx`

**Interfaces:**

- Consumes: `PayloadLayout.MaterializeRepository` and the existing `templates/Karpik.Game` template.
- Produces: `sdk/Karpik.Engine.Templates.<sdkVersion>.nupkg` and `sdk/templates.json`.
- The generated catalog schema is:

```json
{
  "templates": [{
    "id": "karpik-game",
    "name": "Karpik Game",
    "description": "Creates a Karpik game solution.",
    "shortName": "karpik-game",
    "packageFile": "Karpik.Engine.Templates.0.6.0-local.nupkg"
  }]
}
```

- [ ] **Step 1: Add failing package and parameter tests.**

In `EnginePayloadBuilderTests`, build a repository payload and assert `sdk/templates.json` plus its named template `.nupkg` are present. In `ExternalGameCliTests`, assert the `karpikSdkVersion` parameter, the `KarpikSdkVersionPlaceholder` value in template `global.json`, and host alias `--karpik-sdk-version`.

- [ ] **Step 2: Run the new narrow tests.**

Run from `C:\Users\artem\RiderProjects\KarpikEngine`:

```powershell
dotnet test Karpik.Engine.Packager.Tests\Karpik.Engine.Packager.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~EnginePayloadBuilderTests"
dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~Template_metadata"
```

Expected: the new assertions fail because no package, catalog, or SDK-version symbol exists.

- [ ] **Step 3: Add the pack-only template project and source catalog.**

Create a `net10.0` pack-only project with `PackageId` `Karpik.Engine.Templates`, `PackageType` `Template`, `IncludeBuildOutput=false`, `IncludeContentInPack=true`, `ContentTargetFolders=content`, and `<Compile Remove="**\*" />`. Package `templates/Karpik.Game/**` below `content/Karpik.Game/`. Feed its `PackageVersion` from the existing property. Add it to `KarpikEngine.slnx`.

Create `templates/catalog.json` with the shown entry but replace `packageFile` with `packageId: "Karpik.Engine.Templates"`; it is packager source metadata, not generated game content.

- [ ] **Step 4: Parameterize the SDK pin.**

Replace `0.6.0-local` in template `global.json` with `KarpikSdkVersionPlaceholder`. Add this symbol to `template.json`:

```json
"karpikSdkVersion": {
  "type": "parameter",
  "datatype": "text",
  "description": "Exact Karpik.Engine.Sdk version for the generated game.",
  "replaces": "KarpikSdkVersionPlaceholder",
  "defaultValue": "0.6.0-local"
}
```

Create `dotnetcli.host.json` mapping `symbolInfo.karpikSdkVersion.longName` to `karpik-sdk-version` and `shortName` to an empty string.

- [ ] **Step 5: Pack and catalog the templates.**

In `PayloadLayout.MaterializeRepository`, pack `Karpik.Engine.Templates` into an owned build-scratch folder:

```text
dotnet pack Karpik.Engine.Templates/Karpik.Engine.Templates.csproj -c Release --no-restore -m:1 -nr:false -p:PackageVersion=<sdkVersion> -o <templateOutput>
```

Require exactly one `Karpik.Engine.Templates.*.nupkg`, copy it to existing `sdkOutput`, validate the source catalog's unique IDs/short names/package ID, replace `packageId` with the actual copied `packageFile`, and write UTF-8 `sdkOutput/templates.json`. Do not change payload layout version or make the catalog mandatory for validation.

- [ ] **Step 6: Prove standard template generation.**

Add one integration test that packs the template, installs its local `.nupkg` using a test-owned `--debug:custom-hive`, runs:

```text
dotnet new karpik-game --name SmokeGame --output <test-output> --karpik-sdk-version 0.6.0-local
```

and checks `SmokeGame.slnx` and the exact generated `global.json` SDK version. Run:

```powershell
dotnet test Karpik.Engine.Packager.Tests\Karpik.Engine.Packager.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~EnginePayloadBuilderTests"
dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~Template_"
```

Expected: all selected tests pass without using the user hive.

- [ ] **Step 7: Commit.**

```powershell
git add Karpik.Engine.Templates templates Karpik.Engine.Packager/PayloadLayout.cs Karpik.Engine.Packager.Tests/EnginePayloadBuilderTests.cs Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs KarpikEngine.slnx
git commit -m "feat: package SDK project templates"
```

### Task 2: Enumerate valid installed SDK payloads

**Files:**

- Modify: `Karpik.Engine.Tooling/EngineInstallationResolver.cs`
- Modify: `Karpik.Engine.Tooling.Tests/GlobalJsonAndResolverTests.cs`

**Interfaces:**

```csharp
public sealed record InstalledEngineInstallation(
    string InstallationRoot,
    EngineInstallationManifest Manifest);

public IReadOnlyList<InstalledEngineInstallation> ListInstalled();
```

- [ ] **Step 1: Write the failing resolver tests.**

Create fixtures for valid `0.6.0-local`, valid `0.7.0`, one invalid installation, and a missing store. Assert `ListInstalled()` yields only the two valid roots in `0.7.0`, `0.6.0-local` order and returns an empty collection for the missing store.

- [ ] **Step 2: Run tests to confirm failure.**

```powershell
dotnet test Karpik.Engine.Tooling.Tests\Karpik.Engine.Tooling.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~GlobalJsonAndResolverTests"
```

Expected: compile failure because the record and method do not exist.

- [ ] **Step 3: Add the smallest API to the existing resolver.**

Extract the current default-store traversal into `ListInstalled`. If the store is missing or a reparse point, return empty. Run `_validator.Validate(directory)`, retain only valid manifest-bearing roots, normalize paths, and sort descending by parsed numeric `major.minor.patch`; stable ranks before the matching prerelease, and non-parseable safe values sort ordinally after numeric versions. Use `Version.TryParse` and a private comparer; do not add a package dependency.

- [ ] **Step 4: Run the full tooling suite and commit.**

```powershell
dotnet test Karpik.Engine.Tooling.Tests\Karpik.Engine.Tooling.Tests.csproj -m:1 -nr:false --no-restore
git add Karpik.Engine.Tooling/EngineInstallationResolver.cs Karpik.Engine.Tooling.Tests/GlobalJsonAndResolverTests.cs
git commit -m "feat: list installed engine SDKs"
```

Expected: all tooling tests pass.

### Task 3: Add strict catalog reading and transactional project creation

**Files:**

- Create: `Karpik.Launcher/Models/ProjectTemplate.cs`
- Create: `Karpik.Launcher/Models/ProjectCreationRequest.cs`
- Create: `Karpik.Launcher/Services/ProjectTemplateCatalogReader.cs`
- Create: `Karpik.Launcher/Services/DotNetTemplateProcessRunner.cs`
- Create: `Karpik.Launcher/Services/ProjectCreationService.cs`
- Create: `Karpik.Launcher.Tests/ProjectTemplateCatalogReaderTests.cs`
- Create: `Karpik.Launcher.Tests/ProjectCreationServiceTests.cs`
- Modify: `Karpik.Launcher.Tests/TestWorkspace.cs`

**Interfaces:**

```csharp
public sealed record ProjectTemplate(
    string Id, string Name, string Description, string ShortName, string PackageFile);

public sealed record ProjectCreationRequest(
    string InstallationRoot, string SdkVersion, ProjectTemplate Template,
    string ParentDirectory, string ProjectName);

public sealed record ProjectCreationResult(
    bool IsSuccess, string Message, string? SolutionPath = null);

internal interface IDotNetTemplateProcessRunner
{
    Task RunAsync(string workingDirectory, IReadOnlyList<string> arguments,
        string customHive, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing reader tests.**

Test a valid Task-1 catalog plus invalid JSON, missing field, duplicate `id`, duplicate `shortName`, a separator in `packageFile`, missing package, and an escaping package path. Extend `TestWorkspace.CreateInstallation` with optional catalog/package fixture arguments so the initial fixture is hashed before individual test corruption.

- [ ] **Step 2: Run reader tests to confirm failure.**

```powershell
dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~ProjectTemplateCatalogReaderTests"
```

Expected: compile failure because the catalog model and reader do not exist.

- [ ] **Step 3: Implement catalog reading.**

Use `JsonDocument`. Require a `templates` array and five nonempty strings per item. Require safe unique IDs and short names; require `packageFile` to be a safe `.nupkg` filename; normalize `sdk/packageFile`, prove it is below `sdk`, and require the file. Return an empty list when the catalog is absent, and an actionable failure for all malformed catalogs.

- [ ] **Step 4: Write failing transaction tests.**

Use a recording `IDotNetTemplateProcessRunner` fake that writes output only for the creation call. Test successful output, blank/traversal/separator names, nonexistent parent, existing target, process failure, zero solutions, two solutions, and mismatched generated SDK. Success must assert the exact `--karpik-sdk-version` argument and deleted hive; every failure must assert no target or owned staging directory exists.

- [ ] **Step 5: Run tests to confirm failure.**

```powershell
dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~ProjectCreationServiceTests"
```

Expected: compile failure because the request, result, runner, and service do not exist.

- [ ] **Step 6: Implement process execution and publication.**

The production runner uses `ProcessStartInfo.ArgumentList`, executable `dotnet`, and `DOTNET_NOLOGO=1`. `ProjectCreationService` creates `<parent>/.karpik-create-<guid>` plus `<temp>/karpik-launcher-template-hives/<guid>`, invokes:

```text
new install <sdk/packageFile> --debug:custom-hive <hive>
new <shortName> --name <name> --output <staging> --karpik-sdk-version <sdkVersion> --debug:custom-hive <hive> --no-update-check
```

Validate one top-level `*.slnx`; read staging `global.json` with `GlobalJsonSdkVersionReader`; require the exact requested version; then publish via `Directory.Move(staging, target)`. In `finally`, delete only normalized generated paths under their owned roots. Never delete parent or target.

- [ ] **Step 7: Run focused launcher service tests and commit.**

```powershell
dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~ProjectTemplateCatalogReaderTests|FullyQualifiedName~ProjectCreationServiceTests"
git add Karpik.Launcher/Models Karpik.Launcher/Services Karpik.Launcher.Tests/TestWorkspace.cs Karpik.Launcher.Tests/ProjectTemplateCatalogReaderTests.cs Karpik.Launcher.Tests/ProjectCreationServiceTests.cs
git commit -m "feat: create projects from SDK templates"
```

Expected: all failure cases leave no project target and the successful case publishes one valid solution.

### Task 4: Add the dialog and route success through the existing launcher flow

**Files:**

- Create: `Karpik.Launcher/ViewModels/CreateProjectViewModel.cs`
- Create: `Karpik.Launcher/CreateProjectWindow.axaml`
- Create: `Karpik.Launcher/CreateProjectWindow.axaml.cs`
- Modify: `Karpik.Launcher/ViewModels/ILauncherViewModel.cs`
- Modify: `Karpik.Launcher/ViewModels/LauncherViewModel.cs`
- Modify: `Karpik.Launcher/LauncherView.axaml`
- Modify: `Karpik.Launcher/LauncherView.axaml.cs`
- Modify: `Karpik.Launcher/MainWindow.axaml.cs`
- Modify: `Karpik.Launcher/Localization/Strings.resx`
- Modify: `Karpik.Launcher/Localization/Strings.ru-ru.resx`
- Modify: `Karpik.Launcher.Tests/LauncherViewModelTests.cs`

**Interfaces:**

```csharp
public sealed class CreateProjectViewModel : ReactiveObject
{
    public ObservableCollection<InstalledEngineInstallation> InstalledSdks { get; }
    public ObservableCollection<ProjectTemplate> Templates { get; }
    public InstalledEngineInstallation? SelectedSdk { get; set; }
    public ProjectTemplate? SelectedTemplate { get; set; }
    public string ProjectName { get; set; }
    public string ParentDirectory { get; set; }
    public bool OpenAfterCreation { get; set; }
    public ICommand BrowseParentDirectoryCommand { get; }
    public ICommand CreateCommand { get; }
}
```

`LauncherViewModel` exposes `ShowCreateProjectCommand` and receives the child result through one helper that adds to `ProjectRegistry`, reloads Recent projects, and calls existing `LaunchAsync` only when `OpenAfterCreation` is true.

- [ ] **Step 1: Add failing view-model tests.**

With two valid fixture SDKs and a fake creation service, assert newest SDK selection, template refresh on SDK change, selected request forwarding, recent registration after every success, no editor call when unchecked, one editor call when checked, status on failure, and busy state during creation.

- [ ] **Step 2: Run the current launcher tests to confirm failure.**

```powershell
dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~LauncherViewModelTests"
```

Expected: compile failure because create-project commands and state do not exist.

- [ ] **Step 3: Implement the view-model path.**

Construct `CreateProjectViewModel` from `EngineInstallationResolver.ListInstalled()`, the catalog reader, creation service, and existing `IStorageProvider`. Select the first SDK; when it changes, replace templates with the selected payload catalog and select its first entry. The browse command calls `OpenFolderPickerAsync` and uses the local folder path. Keep parent `IsBusy` true around creation and optional launch.

- [ ] **Step 4: Implement the modal view.**

Add a `CreateProjectWindow` with SDK/template ComboBoxes, name and parent TextBoxes, Browse button, `Open after creation` CheckBox, validation/status text, Create and Cancel buttons. Add one `Create project...` action beside Open in `LauncherView.axaml`; code-behind opens the dialog with `TopLevel.GetTopLevel(this)`. Update `MainWindow` construction to pass one creation service. Add neutral and Russian resource keys for every new control and diagnostic; do not hardcode UI English except catalog-supplied metadata.

- [ ] **Step 5: Run tests, build, manually check, and commit.**

```powershell
dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj -m:1 -nr:false --no-restore
dotnet build Karpik.Launcher\Karpik.Launcher.csproj -m:1 -nr:false --no-restore
git add Karpik.Launcher Karpik.Launcher.Tests/LauncherViewModelTests.cs
git commit -m "feat: add launcher project creation dialog"
```

Manual acceptance: use two test payloads, verify newest default and template refresh, reject an existing target without changes, create unchecked without launching, and create checked with the exact matching editor.

### Task 5: Run final package-to-launcher acceptance

**Files:**

- Modify: `plans/launcher-project-creation-execplan.md`
- Modify only if validation identifies a defect: files named in Tasks 1-4.

- [ ] **Step 1: Build a disposable payload.**

```powershell
dotnet run --project Karpik.Engine.Packager\Karpik.Engine.Packager.csproj --no-restore -- --source . --output artifacts\karpik-home --engine-version 0.6.0-dev --sdk-version 0.6.0-local
```

Expected: the payload contains `sdk/templates.json` and `Karpik.Engine.Templates.0.6.0-local.nupkg`.

- [ ] **Step 2: Run all target suites.**

```powershell
dotnet test Karpik.Engine.Tooling.Tests\Karpik.Engine.Tooling.Tests.csproj -m:1 -nr:false --no-restore
dotnet test Karpik.Engine.Packager.Tests\Karpik.Engine.Packager.Tests.csproj -m:1 -nr:false --no-restore
dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~Template_"
dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj -m:1 -nr:false --no-restore
git diff --check
```

Expected: all selected tests pass and no whitespace error is reported.

- [ ] **Step 3: Record result and commit plan progress.**

Update Progress, discoveries, and outcomes with the exact command results; then run:

```powershell
git add plans/launcher-project-creation-execplan.md
git commit -m "docs: record launcher project creation validation"
```

## Validation and Acceptance

- A payload includes a hash-covered package and catalog.
- The local package in a custom hive generates a project pinned to the requested exact SDK.
- Resolver output is valid-only and newest-first.
- Malformed/missing/escaping catalogs and unsafe/existing destinations are rejected without user data loss.
- The generated root contains exactly one solution and matching `global.json`.
- Every success appears in Recent projects; only checked open-now starts an editor.
- Targeted tests and launcher build pass; no runtime hot path changes exist.

## Idempotence and Recovery

Each creation uses unique owned hive and sibling staging paths. An existing target fails before process invocation. A normal failure or cancellation deletes only owned paths, never the selected parent or target. Payload publication remains immutable and hash-protected by the existing packager. Revert individual milestone commits to roll back; do not use `git reset --hard` or touch unrelated user worktree changes.

## Artifacts and Notes

- Design: `docs/superpowers/specs/2026-09-14-launcher-project-creation-design.md`.
- Payload contract: `docs/02_ADR/versioned-engine-sdk-and-external-game-projects.md`.
- The executor records live evidence in this plan before marking Progress complete.
