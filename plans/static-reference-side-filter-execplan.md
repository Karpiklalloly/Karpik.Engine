# Side-safe static module payload references Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Prevent catalog-incompatible module primary DLLs from re-entering static Client/Server compiler graphs through the payload-layout-v3 `shared/` directory.

**Architecture:** Keep `modules.catalog` as the only source of module side information. `ResolveKarpikStaticReferencesTask` will derive catalog primary assembly filenames and skip those filenames while collecting top-level shared payloads; selected primary assemblies still come from their canonical module directories. The test uses generated generic catalog IDs and checks both exclusion of a catalog primary and retention of an ordinary shared dependency.

**Tech Stack:** C#/.NET 10, MSBuild task API, xUnit, `EngineModuleCatalog`, `ModuleLayoutPolicy`, PowerShell SDK packager.

**Spec:** `docs/superpowers/specs/2026-09-20-static-reference-side-filter-design.md`

## Global Constraints

- The catalog remains the single source of truth for module side.
- The regression test must not mention `Graphics.Core`, `Window.Core`, or any concrete product module name.
- Existing reparse-point, runner-assembly, deduplication, and path-safety checks remain intact.
- Do not inspect or load assembly metadata to infer module side.
- Do not change the Roslyn generator, module catalog format, runtime loader, or DI registrations.
- No edited code runs in frame, ECS, network, render, or fixed-tick hot paths.
- Use `dotnet build` and `dotnet test` with `-m:1 -nr:false`.

## Review Focus

- A catalog primary copied into `shared/` is excluded even when its assembly has no `KarpikSide` metadata; the generic regression test covers this.
- An ordinary shared dependency with a non-catalog filename remains in `PayloadAssemblies`; the same regression test covers this.
- Shared catalog primaries are not accidentally added twice; existing deduplication and side tests remain the guard.
- Case-insensitive Windows filename comparison still excludes catalog primaries; the implementation uses the repository's existing ordinal-ignore-case comparer.
- Missing, malformed, or unsafe catalogs still fail through the existing task error path; existing invalid-catalog tests remain the guard.

---

## Progress

- [x] (2026-09-20) Conversational design approved by the developer.
- [x] (2026-09-20) Written spec approved by the developer.
- [x] (2026-09-20) Plan written and self-reviewed.
- [x] Add the generic failing regression test.
- [x] Implement catalog-primary filtering.
- [x] Run targeted and solution verification.
- [x] Publish a development SDK and repin `D:\Games\SSSuperGame`.

## Surprises & Discoveries

- Observation: The current generator has a `KarpikSide` metadata filter, but built-in module projects use `Microsoft.NET.Sdk` and do not reliably emit that metadata.
  Evidence: `Modules/Client/Graphics/Graphics.Core/Graphics.Core.csproj` and `Modules/Shared/LoggerModule/LoggerModule.csproj` do not declare `KarpikSide` or use `Karpik.Engine.Sdk`.
- Observation: `ResolveKarpikStaticReferencesTask` appends every top-level `shared/*.dll` after selecting catalog references.
  Evidence: `Karpik.Engine.Sdk.Tasks/ResolveKarpikStaticReferencesTask.cs`, `CollectSharedPayloadAssemblies`.
- Observation: The engine checkout is a normal `content-pipeline` branch and `.git` is read-only in this sandbox, so source commits may require a later user-side commit or elevated permission.
  Evidence: `git commit` failed with permission denied creating `.git/index.lock`.

## Decision Log

- Decision: Filter catalog primary filenames in the SDK task instead of making the generator reject assemblies with missing side metadata.
  Rationale: the task has validated side information and can remove the bad reference before Roslyn sees it; rejecting unknown metadata would break legitimate third-party shared payloads.
  Date/Author: 2026-09-20 / Codex.
- Decision: Test generic catalog/payload behavior rather than concrete engine module names.
  Rationale: the invariant belongs to the payload contract and must remain valid when the module catalog changes.
  Date/Author: 2026-09-20 / Developer and Codex.

## Outcomes & Retrospective

- The generic red test failed before the production change because a catalog
  primary copied into `shared/` was emitted as a payload assembly.
- The green implementation filters catalog primary filenames case-insensitively
  while retaining ordinary shared dependencies. The task suite passes: 81
  passed, 4 skipped, 0 failed.
- The fresh SDK `0.6.0-local-20260920-134822` was published and selected by
  `D:\Games\SSSuperGame`. The final external build with an explicit engine
  root completed with 0 warnings and 0 errors, and the server smoke passed the
  original wrong-side DI failure.
- The full engine solution also compiled in the approved elevated rerun, but
  the ordinary sandboxed rerun was blocked by Avalonia's permission to write
  `C:\Users\artem\AppData\Local\AvaloniaUI\BuildServices\buildtasks.log`.
  This is an environment limitation, not a failure in the changed task.

## Context and Orientation

`ResolveKarpikStaticReferencesTask.Resolve` reads and validates
`modules/modules.catalog`, selects `Shared + Client` or `Shared + Server`,
resolves selected primary assemblies under `modules/<ModuleId>/`, and collects
transitive payload DLLs. Payload layout v3 also puts shared dependencies at
`<engine>/shared/`. The existing top-level scan has no knowledge of which files
are catalog primary assemblies, so a duplicate of an opposite-side primary can
be added as a compiler reference. Static composition then discovers installers
from that wrong-side assembly.

The task is build-time tooling only. The target game already has a temporary
content-manifest copy workaround from the preceding build investigation; this
plan does not broaden into content packaging or restore the deleted headless
bindings workaround.

## Real-Time Assessment

The change runs only during MSBuild static reference resolution and has no
runtime or hot-path impact. It adds one bounded filename set for the catalog
(the catalog itself is capped by `EngineModuleCatalog`) and one O(1) lookup per
top-level shared DLL. No frame, ECS, serialization, network, physics, or render
code is changed. Client/Server/Shared boundaries become stricter because
opposite-side primary assemblies are removed before compilation.

## Plan of Work

### Task 1: Add the generic red regression test

**Files:**

- Modify: `Karpik.Engine.Sdk.Tasks.Tests/ResolveKarpikStaticReferencesTaskTests.cs`

**Interfaces:**

- Consumes: existing `Tree.Add`, `Tree.WriteCatalog`, temporary module layout, and `ResolveKarpikStaticReferencesTask` outputs.
- Produces: `Execute_ExcludesCatalogPrimaryAssemblyFromSharedPayload`, a failing test that defines the required behavior without product module names.

- [ ] **Step 1: Add the test method.**

Use generic IDs and the existing temporary assembly helper:

```csharp
[Fact]
public void Execute_ExcludesCatalogPrimaryAssemblyFromSharedPayload()
{
    using var tree = new Tree();
    tree.Add("SharedModule", typeof(EngineModuleCatalog).Assembly.Location, EngineModuleSide.Shared);
    tree.Add("ClientModule", typeof(ResolveKarpikStaticReferencesTask).Assembly.Location, EngineModuleSide.Client);
    tree.WriteCatalog();

    string clientPrimary = Path.Combine(tree.Root, "modules", "ClientModule", "ClientModule.dll");
    Directory.CreateDirectory(Path.Combine(tree.Root, "shared"));
    File.Copy(clientPrimary, Path.Combine(tree.Root, "shared", "ClientModule.dll"));
    File.Copy(
        typeof(ResolveKarpikStaticReferencesTask).Assembly.Location,
        Path.Combine(tree.Root, "shared", "OrdinarySharedDependency.dll"));

    var task = new ResolveKarpikStaticReferencesTask
    {
        BuildEngine = new Engine(),
        EngineRoot = tree.Root,
        Side = "Server"
    };

    Assert.True(task.Execute());
    Assert.DoesNotContain(task.PayloadAssemblies, item =>
        item.ItemSpec.EndsWith("ClientModule.dll", StringComparison.OrdinalIgnoreCase));
    Assert.Contains(task.PayloadAssemblies, item =>
        item.ItemSpec.EndsWith("OrdinarySharedDependency.dll", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Run the test before production code changes.**

Run from `C:\Users\artem\RiderProjects\KarpikEngine`:

```powershell
dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~Execute_ExcludesCatalogPrimaryAssemblyFromSharedPayload"
```

Expected result: the test fails because the current shared-directory scan includes `ClientModule.dll` in `PayloadAssemblies`.

### Task 2: Filter catalog primary DLLs in the task

**Files:**

- Modify: `Karpik.Engine.Sdk.Tasks/ResolveKarpikStaticReferencesTask.cs`

**Interfaces:**

- Consumes: validated `EngineModuleCatalogEntry[] catalog` and `ModuleLayoutPolicy.GetPrimaryAssemblyFileName`.
- Produces: `CollectSharedPayloadAssemblies(string engineRoot, HashSet<string> seen, HashSet<string> catalogPrimaryFileNames, List<ITaskItem> collected)` with catalog primary exclusion.

- [ ] **Step 1: Derive primary filenames from the complete validated catalog.**

Immediately after `EngineModuleCatalog.Read(modulesRoot)`, create a case-insensitive set:

```csharp
HashSet<string> catalogPrimaryFileNames = catalog
    .Select(entry => ModuleLayoutPolicy.GetPrimaryAssemblyFileName(entry.ModuleId))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
```

Pass this set to `CollectSharedPayloadAssemblies` after selected module payloads have been collected.

- [ ] **Step 2: Skip catalog primary filenames in the top-level shared scan.**

Extend the helper signature and add the catalog check beside the existing safe-file checks:

```csharp
string fileName = Path.GetFileName(file);
if (!seen.Add(file)
    || catalogPrimaryFileNames.Contains(fileName)
    || fileName.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase)
    || IsReparsePoint(file))
{
    continue;
}
```

Keep selected primary references unchanged: they continue to come from `ResolvePrimaryAssembly(modulesRoot, entry.ModuleId)` and `References`.

- [ ] **Step 3: Run the focused test.**

Run the same filtered `dotnet test` command from Task 1.

Expected result: PASS, with the generic catalog primary excluded and the ordinary shared dependency retained.

### Task 3: Complete SDK and external-game verification

**Files / external state:**

- Verify: `Karpik.Engine.Sdk.Tasks.Tests/ResolveKarpikStaticReferencesTaskTests.cs`.
- Verify: `Karpik.Engine.Sdk/Sdk/Sdk.targets` consumes `PayloadAssemblies` unchanged.
- Update after packaging: `D:\Games\SSSuperGame\global.json` SDK pin.
- Update after packaging: `D:\Games\SSSuperGame\Tests\SSSuperGame.Tests\SSSuperGame.Tests.csproj` test SDK pin.

**Interfaces:**

- Consumes: the green task implementation and the repository packager.
- Produces: a newly installed development engine and an external game build using that exact SDK version.

- [ ] **Step 1: Run the complete task test project.**

```powershell
dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false
```

Expected result: zero failed tests.

- [ ] **Step 2: Build the engine solution.**

```powershell
dotnet build KarpikEngine.slnx -m:1 -nr:false
```

Expected result: exit code 0 with no compilation errors.

- [ ] **Step 3: Publish a fresh local SDK/engine installation.**

From the repository root, run the existing transactional publisher:

```powershell
& .\_scripts\Update-KarpikSdk.ps1
```

Record the emitted `SDK version:` and installation path. Do not hand-edit the version string in the package; use the exact version emitted by the publisher.

- [ ] **Step 4: Repin the external game to the emitted SDK version.**

Update only the existing exact SDK version values in the target project's `global.json` and `Tests/SSSuperGame.Tests/SSSuperGame.Tests.csproj`. Preserve the existing `ServerHeadlessBindings.cs` deletion and the static content-manifest workaround; neither is part of this task's engine-side fix.

- [ ] **Step 5: Build the external game.**

```powershell
dotnet build D:\Games\SSSuperGame\SSSuperGame.slnx -m:1 -nr:false
```

Expected result: exit code 0 with zero warnings and zero errors.

- [ ] **Step 6: Start the server launcher and inspect the first startup window.**

Run the generated server launcher from its output directory with a bounded timeout. Confirm the log no longer contains `IInputSource` or other client graphics/window registrations caused by the wrong-side payload. If a later independent runtime failure appears, record it separately rather than restoring client bindings.

## Milestones

1. Red test proves the current task leaks a catalog primary through `shared/`.
2. Green task test proves generic side-safe payload filtering.
3. Full task tests and engine build pass.
4. Fresh SDK installation is selected by the external game.
5. External solution builds and the server startup moves past the reported wrong-side DI failure.

## Validation and Acceptance

Acceptance requires the focused red/green cycle, complete task tests, an engine
solution build, and an external-game build using the fresh exact SDK version.
The startup smoke must show that the original client-only service resolution
failure is not recreated by static payload references. The test suite must stay
module-name agnostic.

## Idempotence and Recovery

Task tests use unique temporary directories and are safe to rerun. Engine
builds write normal `bin/obj` outputs only. `_scripts/Update-KarpikSdk.ps1`
publishes transactionally and archives prior matching development installs;
the emitted exact version can be restored in the target `global.json` if the
external smoke exposes an unrelated problem. Do not delete the target game or
engine installations.

## Artifacts and Notes

- Design spec: `docs/superpowers/specs/2026-09-20-static-reference-side-filter-design.md`
- Fresh package/install output: emitted by `_scripts/Update-KarpikSdk.ps1`
- External verification log: server launcher stdout/stderr from the fresh SDK
