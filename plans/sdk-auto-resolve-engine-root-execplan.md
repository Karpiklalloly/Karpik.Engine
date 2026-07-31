# Make external game builds resolve their engine installation automatically

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

An external game created from `templates/Karpik.Game` must build with an ordinary `dotnet build` even when the invoking process does not contain `KarpikEngineRoot`. The custom `Karpik.Engine.Sdk` will resolve the exact installed engine payload that provides the SDK version pinned in the game's `global.json`, validate that payload, and use its runner assemblies as compile references. A user can observe success by clearing `KarpikEngineRoot` and building `D:\Games\MyFirstGame`; all projects must compile without manual MSBuild properties.

## Progress

- [x] (2026-07-31) Root cause reproduced: the user-scoped environment variable exists, but a newly used PowerShell process can still have an empty process-scoped `KarpikEngineRoot`.
- [x] (2026-07-31) Design agreed: preserve explicit override priority, otherwise perform exact-version lookup, and fail rather than choose when multiple valid installations match.
- [x] (2026-07-31) Added a tested MSBuild task that adapts `EngineInstallationResolver` to SDK builds; initial RED failed because the task type was absent, then the automatic and ambiguity tests passed.
- [x] (2026-07-31) Wired the task into `Sdk.targets` before engine compile references are created; the target-structure test passed.
- [x] (2026-07-31) Packaged the task's tooling dependency; the fake repository package test passed and a direct package inspection contained `Karpik.Engine.Tooling.dll`.
- [x] (2026-07-31) Extended the external template integration fixture to remove `KarpikEngineRoot` and use an isolated local application-data store.
- [x] (2026-07-31) Published SDK `0.6.0-local` and engine payload `0.6.0-dev-b7644f363d5858243edc8d4acb97cdf3601bf3e26c38998893d691cf58f82b53`; `D:\Games\MyFirstGame` built with an empty process-scoped `KarpikEngineRoot`, zero warnings, and zero errors.
- [x] (2026-07-31) Updated graphify after the code changes and recorded final targeted verification evidence.

## Surprises & Discoveries

- Observation: `Karpik.Engine.Tooling.EngineInstallationResolver` already implements the required policy, including explicit-root precedence, exact SDK-version matching, payload validation, missing-store diagnostics, and deterministic ambiguity rejection.
  Evidence: `Karpik.Engine.Tooling/EngineInstallationResolver.cs` and `Karpik.Engine.Tooling.Tests/GlobalJsonAndResolverTests.cs`.

- Observation: `Karpik.Engine.Sdk/Sdk/Sdk.targets` currently adds `DragonECS.dll` and `Karpik.Engine.Core.dll` only when `$(KarpikEngineRoot)` is non-empty, so an empty inherited environment silently removes required references and produces secondary C# errors.
  Evidence: `_KarpikResolveEngineReferenceAssemblies` in `Karpik.Engine.Sdk/Sdk/Sdk.targets`.

- Observation: the SDK task package currently contains `Karpik.Engine.Sdk.Tasks.dll` and `Karpik.Engine.ProjectModel.dll`, but not `Karpik.Engine.Tooling.dll`.
  Evidence: `Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj`.

- Observation: the opt-in external CLI test requires a retained payload under `artifacts/karpik-home-final/Engines`; it was absent in the current workspace. Creating it inside the sandbox reached Avalonia telemetry and failed because `buildtasks.log` under LocalAppData was not writable.
  Evidence: the first opt-in test stopped in `ResolveRetainedEngineRoot`; the prerequisite packager run failed in `AvaloniaStatsTask` with `UnauthorizedAccessException`.

- Observation: after the developer requested that no more commands run before an explicit readiness checkpoint, the escalated retained-fixture build could not be interrupted through the process backend.
  Evidence: the process backend reported that interrupt was not supported. No further verification command is permitted until the developer approves the readiness checkpoint.

- Observation: the broad opt-in external CLI integration test currently fails before reaching SDK root resolution because its runner build passes a literal `$(MSBuildProjectName)` in `MSBuildProjectExtensionsPath`; DragonECS consequently reads an assets file without its `netstandard2.1` target.
  Evidence: `ExternalGameCliTests.RuntimeBundle_external_template_supports_ordinary_cli_workflow_and_validation_precedence` failed with `NETSDK1005` at `third-parties/DragonECS/DragonECS.csproj`. The direct installed-template acceptance test below passed independently.

## Decision Log

- Decision: Resolve installations inside an MSBuild task and reuse `EngineInstallationResolver`.
  Rationale: this keeps JSON parsing, path safety, payload hashing, and ambiguity policy in the existing tested component instead of duplicating them in MSBuild XML.
  Date/Author: 2026-07-31 / Codex and developer.

- Decision: An explicit non-empty `KarpikEngineRoot` remains the highest-priority override and is validated. An invalid explicit root fails; it does not silently fall back.
  Rationale: explicit configuration must be deterministic and must not hide stale or malicious paths.
  Date/Author: 2026-07-31 / Codex.

- Decision: With no explicit root, zero valid exact matches and multiple valid exact matches both fail the build with a stable Karpik diagnostic.
  Rationale: selecting an arbitrary payload would make builds depend on directory enumeration or timestamps.
  Date/Author: 2026-07-31 / developer.

- Decision: Use an optional `KarpikLocalApplicationDataRoot` MSBuild property only as a controlled installation-store override for tests and portable tooling. Normal consumers leave it empty and use `Environment.SpecialFolder.LocalApplicationData`.
  Rationale: integration tests need an isolated store outside the real user profile, while production builds need the platform default.
  Date/Author: 2026-07-31 / Codex.

## Outcomes & Retrospective

The original reboot-sensitive build failure is fixed. The SDK now resolves a single validated installed engine whose manifest declares the exact MSBuild SDK version selected by `global.json`; a process environment variable is no longer required. An explicit `KarpikEngineRoot` remains supported and takes precedence, while invalid, missing, or ambiguous installations fail through stable diagnostic `KARPIK009` instead of surfacing secondary missing-namespace errors.

Fresh verification on 2026-07-31 produced:

- `Karpik.Engine.Sdk.Tasks.Tests`: 46 passed, 3 Windows symbolic-link checks skipped, 0 failed.
- `Karpik.Engine.Packager.Tests`: 19 passed, 0 failed; this includes the real PowerShell user-environment behavior check and package dependency surface.
- `Update-KarpikSdk.ps1`: exited successfully and published `C:\Users\artem\AppData\Local\Karpik\Engines\0.6.0-dev-b7644f363d5858243edc8d4acb97cdf3601bf3e26c38998893d691cf58f82b53`; the prior payload was archived.
- `D:\Games\MyFirstGame`: with `ProcessKarpikEngineRoot=` empty, all Shared, Client, Server, launcher, and test projects built successfully with 0 warnings and 0 errors.
- `graphify update .`: completed, rebuilding a graph of 10,502 nodes and 18,726 edges.

One broader integration fixture remains red for the unrelated runner intermediate-path issue recorded above. It does not invalidate the direct reproduction, but that fixture must be repaired before claiming the entire opt-in CLI suite is green. The publish script still updates the process and user environment variables for backward compatibility; correctness of ordinary SDK builds no longer depends on either update becoming visible to an already-running shell.

## Context and Orientation

External game projects use `<Project Sdk="Karpik.Engine.Sdk">`. Their `global.json` pins `Karpik.Engine.Sdk` to an exact package version such as `0.6.0-local`. NuGet restores that SDK under its global packages directory, and MSBuild imports `Karpik.Engine.Sdk/Sdk/Sdk.props` and `Sdk.targets`.

`Karpik.Engine.Sdk/Sdk/Sdk.targets` loads tasks from `tools/net10.0/Karpik.Engine.Sdk.Tasks.dll`. Its `_KarpikResolveEngineReferenceAssemblies` target currently constructs compile references from `$(KarpikEngineRoot)\runners\server`. The property normally enters MSBuild through a process environment variable. That is the unreliable boundary this plan removes.

`Karpik.Engine.Tooling/EngineInstallationResolver.cs` accepts the requested MSBuild SDK version, an optional explicit engine root, and an injectable local-application-data root. Without an explicit root it scans `<LocalApplicationData>/Karpik/Engines`, validates each candidate through `EngineInstallationValidator`, and succeeds only for one exact match.

The new adapter task belongs in `Karpik.Engine.Sdk.Tasks` because resolution occurs during an MSBuild target. `Karpik.Engine.Tooling` remains independent of MSBuild and contains the filesystem policy. The SDK NuGet package must include both assemblies so task loading never falls back to repository outputs.

## Real-Time Assessment

This work does not touch `Update`, `FixedUpdate`, ECS `Run`, networking, serialization, rendering, or any runtime hot path. Resolution runs during build before `ResolveAssemblyReferences`. Filesystem enumeration, JSON parsing, hashing, and managed allocations are acceptable there. No Client, Server, or Shared project boundary changes. No tick, data-layout, or concurrency behavior changes. Validation focuses on deterministic diagnostics, package completeness, and external CLI behavior.

## Plan of Work

First add `ResolveKarpikEngineRootTask` to `Karpik.Engine.Sdk.Tasks`. The task exposes required `SdkVersion`, optional `ExplicitRoot`, optional `LocalApplicationDataRoot`, and output `ResolvedRoot`. It constructs `EngineInstallationResolver`, calls `Resolve`, writes one stable Karpik error when resolution fails, and sets the output only on success. Unit tests use real temporary installations and a real MSBuild test engine to cover automatic exact lookup, explicit override precedence, no match, invalid explicit root, and multiple matches.

Next add a project reference from `Karpik.Engine.Sdk.Tasks` to `Karpik.Engine.Tooling`. Update `Karpik.Engine.Sdk.csproj` so `Karpik.Engine.Tooling.dll` is copied into `tools/net10.0/` beside the task assembly. Extend package-surface tests to assert this dependency is present.

Then update `Karpik.Engine.Sdk/Sdk/Sdk.targets`. For runtime projects, `_KarpikResolveEngineReferenceAssemblies` invokes the new task before creating `Reference` items. The pinned SDK version is derived from the parent directory of `$(MSBuildThisFileDirectory)`, which is the NuGet SDK version directory selected from `global.json`. The task receives `$(KarpikEngineRoot)` as the explicit override and `$(KarpikLocalApplicationDataRoot)` as an optional isolated local-data root. Its output overwrites `KarpikEngineRoot` for the remainder of the project build. Compile references are created only after successful resolution.

Finally adapt `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs`. Publish the test engine under an isolated `<temporary>/local/Karpik/Engines` store, set `KarpikLocalApplicationDataRoot` to `<temporary>/local`, remove `KarpikEngineRoot` from the environment before the ordinary restore/build/test/publish workflow, and assert the evaluated root equals the published payload. Preserve a separate explicit-root scenario to prove override compatibility. Add failure scenarios for a missing exact installation and two exact matches, both before C# compilation.

## Milestones

### Milestone 1: Tested resolver task

Write failing unit tests in `Karpik.Engine.Sdk.Tasks.Tests` for the task's observable output and diagnostics. Implement the smallest adapter over `EngineInstallationResolver`. End with:

    dotnet test Karpik.Engine.Sdk.Tasks.Tests/Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false

Expected observation: all task tests pass; automatic resolution returns the exact temporary installation; ambiguity produces one stable resolution diagnostic.

### Milestone 2: SDK target and package wiring

Add task invocation, package `Karpik.Engine.Tooling.dll`, and extend package-surface assertions. End with:

    dotnet test Karpik.Engine.Packager.Tests/Karpik.Engine.Packager.Tests.csproj -m:1 -nr:false
    dotnet pack Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj -c Debug -m:1 -nr:false --no-restore

Expected observation: tests pass and the `.nupkg` contains `tools/net10.0/Karpik.Engine.Tooling.dll`.

### Milestone 3: External no-environment workflow

Change the opt-in external integration fixture so the main game workflow has no process `KarpikEngineRoot`. End with:

    $env:KARPIK_RUN_EXTERNAL_SDK_INTEGRATION = "1"
    dotnet test Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --filter "FullyQualifiedName~RuntimeBundle_external_template_supports_ordinary_cli_workflow_and_validation_precedence"

Expected observation: restore, build, test, publish, runtime bundle checks, and failure-precedence checks all pass against the isolated exact-version installation.

### Milestone 4: Installed-template acceptance

Run `Update-KarpikSdk.ps1`, clear only the current process variable, and build the user's generated solution:

    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\Update-KarpikSdk.ps1
    Remove-Item Env:KarpikEngineRoot -ErrorAction SilentlyContinue
    dotnet build D:\Games\MyFirstGame\MyFirstGame.slnx -m:1 -nr:false

Expected observation: the build succeeds with zero warnings and zero errors while `$env:KarpikEngineRoot` remains absent before invocation.

## Concrete Steps

All commands run from `C:\Users\artem\RiderProjects\KarpikEngine` unless a command specifies another working directory.

1. Add task-level RED tests and run the filtered test to prove the missing task behavior fails.
2. Add `ResolveKarpikEngineRootTask.cs` and the `Karpik.Engine.Tooling` project reference; rerun the filtered tests to GREEN.
3. Add target-level/package assertions and run them RED.
4. Modify `Sdk.targets` and `Karpik.Engine.Sdk.csproj`; rerun task and package tests GREEN.
5. Add the no-environment external integration assertions and run the opt-in test RED against the old packed target behavior if needed, then GREEN against the new package.
6. Run all three targeted test projects using single-node MSBuild with node reuse disabled.
7. Publish the local SDK, clear the process variable, and build `D:\Games\MyFirstGame`.
8. Run `graphify update .` and record final evidence in this plan.

## Validation and Acceptance

Acceptance requires all of the following observable results:

- `ResolveKarpikEngineRootTask` returns an exact validated installation for an empty explicit root.
- A valid explicit root takes precedence over the default store.
- An invalid explicit root, missing store, no exact match, corrupt matching payload, and multiple valid exact matches fail with a stable Karpik diagnostic and the resolver message.
- The SDK package contains every managed dependency required to load the task.
- A generated external game completes restore, build, test, and publish without `KarpikEngineRoot` in its process environment.
- `D:\Games\MyFirstGame` builds after the local SDK is republished and the current process variable is removed.
- Existing explicit-root launcher/editor workflows continue to pass.

## Idempotence and Recovery

Unit and integration tests use owned temporary directories and restore any changed environment state. They are safe to rerun. `Update-KarpikSdk.ps1` publishes content-hash-qualified development payloads atomically and archives previous installations only after validating the new payload; rerunning it is the supported recovery path. If packing or integration fails, do not delete user installations manually. Fix the failing milestone, rerun its targeted tests, then rerun the publish script. The explicit `-p:KarpikEngineRoot=<valid root>` override remains available as a temporary recovery mechanism.

## Artifacts and Notes

- Architecture source: `docs/02_ADR/versioned-engine-sdk-and-external-game-projects.md`.
- Existing resolver tests: `Karpik.Engine.Tooling.Tests/GlobalJsonAndResolverTests.cs`.
- External acceptance fixture: `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs`.
- User reproduction: `D:\Games\MyFirstGame`.
