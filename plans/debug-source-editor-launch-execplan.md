# Debug source editor launch

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Debug build of `Karpik.Launcher` starts the matching `Karpik.Editor` from this checkout, while the external game's engine runtime remains the exact selected SDK installation.

**Architecture:** `Karpik.Launcher.csproj` embeds the Debug editor output directory as assembly metadata. `MainWindow` passes that optional directory to `EditorResolver`. The resolver always validates the game's exact SDK installation; only the editor executable, DLL and working directory come from the checkout in Debug. Release has no metadata and continues to select the editor from the SDK payload.

**Tech Stack:** .NET 10, Avalonia, MSBuild assembly metadata, xUnit.

**Spec:** This ExecPlan is the approved design record for this bounded launcher/editor development workflow.

## Global Constraints

- Do not alter game project SDK resolution, runner/module ownership, or Client/Server/Shared boundaries.
- In Debug, fail with an actionable diagnostic when the source editor output is missing; never silently fall back to an installed editor.
- In Release, preserve the existing versioned-SDK editor selection exactly.
- Do not add work to runtime hot paths.

## Purpose / Big Picture

Engine developers can run `Karpik.Launcher` under Debug and immediately receive the current `Karpik.Editor` binary without publishing an SDK payload after each editor change. The project selected by the launcher still pins and validates its installed SDK through `global.json`; `KarpikEngineRoot` passed to the editor remains that validated installation. A developer can observe success by changing the Debug editor, building launcher/editor, opening an external game, and seeing the current editor process start while its runtime bundles resolve from the selected SDK.

## Progress

- [x] (2026-09-13) Design approved: Debug uses a checkout editor; SDK owns the runtime.
- [x] (2026-09-13) Added and observed red resolver/process-host tests.
- [x] (2026-09-13) Implemented metadata-based Debug editor selection.
- [x] (2026-09-13) Launcher tests passed (16/16); Debug editor and launcher builds passed.
- [x] (2026-09-13) External MyFirstGame3 build process completed and refreshed client outputs; the shell lost its final MSBuild stdout, so the developer manual Debug launch remains the definitive end-to-end check.

## Surprises & Discoveries

- Observation: `EditorResolver` currently derives both entry point and working directory from the selected installation.
  Evidence: `Karpik.Launcher/Services/EditorResolver.cs` and `Karpik.Launcher/Services/EditorProcessHost.cs`.
- Observation: a checkout is not a valid `KarpikEngineRoot` installation.
  Evidence: `docs/02_ADR/versioned-engine-sdk-and-external-game-projects.md` requires a validated versioned payload.

## Decision Log

- Decision: Override only the editor host binary in Debug; retain SDK installation resolution for runtime ownership.
  Rationale: This makes editor iteration immediate without invalidating the installed-SDK contract of external games.
  Date/Author: 2026-09-13 / AI assistant and developer.
- Decision: Embed the source editor directory as Debug-only assembly metadata instead of deriving it from `AppContext.BaseDirectory`.
  Rationale: MSBuild already owns the checkout root and metadata remains valid with a custom launcher output path.
  Date/Author: 2026-09-13 / AI assistant and developer.

## Outcomes & Retrospective

Debug launcher metadata points to `Karpik.Editor/bin/Debug/net10.0`. Resolver tests prove this directory supplies only the editor process and working directory; the validated SDK installation remains `KarpikEngineRoot`. Missing source output produces `MissingEditorEntryPoint`, never an SDK fallback. Release has no metadata and keeps SDK editor selection.

## Context and Orientation

`Karpik.Launcher/MainWindow.axaml.cs` constructs `EditorProcessHost`. The host calls `EditorResolver.Resolve`, then starts the returned executable and currently sets the child process working directory to `<installation>/editor`. `EditorResolver` reads the external game's `global.json`, uses `EngineInstallationResolver` to validate the exact payload, and returns `EditorLaunchDescriptor` containing the executable selection.

`Karpik.Launcher/Karpik.Launcher.csproj` imports `Directory.Build.props`, which exposes `$(KarpikRepositoryRoot)`. MSBuild can emit an `AssemblyMetadataAttribute` only in Debug with key `Karpik.DebugEditorDirectory` and value `$(KarpikRepositoryRoot)Karpik.Editor\\bin\\$(Configuration)\\net10.0`. `MainWindow` reads that metadata and passes it to the resolver. An absent value means Release behavior.

The descriptor must carry `WorkingDirectory` as the directory containing the actual chosen editor. This keeps `dotnet <source-editor.dll>` and the source apphost beside their dependency files; it avoids mixing the source editor executable with the SDK editor directory.

## Real-Time Assessment

This work affects desktop launcher/editor startup only. It does not execute in `Update`, `FixedUpdate`, ECS systems, serialization, networking, or rendering. Process setup may allocate and perform I/O, as it already does. No gameplay data layout, tick behavior, locks, or Client/Server/Shared runtime dependencies change. Validation is xUnit resolver/host coverage plus a Debug launcher/editor manual run against an external game.

## Plan of Work

First write tests that express three cases: a supplied Debug source directory wins for editor entry point and working directory while the SDK root remains selected; a supplied but missing directory returns `MissingEditorEntryPoint`; no supplied directory keeps SDK resolution. Extend the host test to prove it uses `descriptor.WorkingDirectory`.

Then add the Debug-only assembly metadata in `Karpik.Launcher.csproj`. Add an optional `debugEditorDirectory` constructor argument to `EditorResolver`. After SDK validation, have it resolve `Karpik.Editor.exe` (or `Karpik.Editor` on non-Windows), otherwise `Karpik.Editor.dll` through `dotnet`, in the debug directory. If neither exists, return the existing missing-entry-point diagnostic naming that directory. With no debug directory, retain the existing installation editor lookup. Populate the descriptor's new `WorkingDirectory` in both paths.

Finally, make `MainWindow` read the `Karpik.DebugEditorDirectory` assembly metadata and construct `EditorProcessHost(new EditorResolver(debugEditorDirectory: value))`. Update `EditorProcessHost` to use `descriptor.WorkingDirectory` when making `EditorProcessStartRequest`.

## Milestones

### Milestone 1: Resolver and host contract

Write red tests in `Karpik.Launcher.Tests/EditorResolverTests.cs` and `Karpik.Launcher.Tests/EditorProcessHostTests.cs`. Verify a debug directory takes precedence only for the editor binary and working directory; `InstallationRoot` is still the exact validated SDK installation. Run:

    dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~EditorResolverTests|FullyQualifiedName~EditorProcessHostTests"

Expected before implementation: the Debug-priority assertion fails.

### Milestone 2: Debug metadata and selection

Modify `Karpik.Launcher/Karpik.Launcher.csproj`, `Karpik.Launcher/MainWindow.axaml.cs`, `Karpik.Launcher/Services/EditorResolver.cs`, and `Karpik.Launcher/Services/EditorProcessHost.cs`. Rerun the Milestone 1 command. Expected: all selected tests pass.

### Milestone 3: Build and manual acceptance

Build launcher and editor in Debug, launch an external game from the Debug launcher, and confirm process selection in the source editor output. Run the complete launcher tests and an external game build with the selected SDK root. Record results in `Progress` and `Outcomes & Retrospective`.

## Concrete Steps

1. In `Karpik.Launcher.Tests/EditorResolverTests.cs`, create a temporary source editor directory containing `Karpik.Editor.dll`; construct `EditorResolver` with it and an `EngineInstallationResolver` rooted in `TestWorkspace`. Assert `Descriptor.FileName == "dotnet"`, `Descriptor.PrefixArguments` contains the source DLL, `Descriptor.WorkingDirectory` is the source directory, and `Descriptor.InstallationRoot` is the test installation.
2. In the same test file, pass a nonexistent source editor directory and assert `EditorResolutionCode.MissingEditorEntryPoint` and an error containing that directory. Construct a resolver without the optional directory and retain the SDK-entry-point assertion.
3. In `Karpik.Launcher.Tests/EditorProcessHostTests.cs`, supply a descriptor whose `WorkingDirectory` differs from its installation's editor folder, run through the recording process runner, and assert the request uses that descriptor field.
4. Run the Milestone 1 test command and observe the expected failure.
5. In `Karpik.Launcher/Karpik.Launcher.csproj`, add Debug-only `AssemblyMetadataAttribute` item metadata with key `Karpik.DebugEditorDirectory` and value `$(KarpikRepositoryRoot)Karpik.Editor\\bin\\$(Configuration)\\net10.0`.
6. In `Karpik.Launcher/MainWindow.axaml.cs`, add a private helper reading that metadata from `typeof(MainWindow).Assembly`; pass the nullable value to `new EditorResolver(debugEditorDirectory: ...)` when constructing `EditorProcessHost`.
7. In `Karpik.Launcher/Services/EditorResolver.cs`, add the optional source directory field and a small entry-point resolver shared by SDK and Debug paths. Add `WorkingDirectory` to `EditorLaunchDescriptor`. Select the Debug directory only when supplied, and return a missing-entry-point result rather than falling back when it has no supported editor host.
8. In `Karpik.Launcher/Services/EditorProcessHost.cs`, replace `Path.Combine(descriptor.InstallationRoot, "editor")` with `descriptor.WorkingDirectory`.
9. Rerun Milestone 1 tests. Then run:

    dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj --no-restore -m:1 -nr:false

    dotnet build Karpik.Launcher\Karpik.Launcher.csproj --no-restore -m:1 -nr:false

10. Build both Debug hosts and manually open `D:\Games\MyFirstGame3\MyFirstGame3.slnx` through `Karpik.Launcher\bin\Debug\net10.0\Karpik.Launcher.exe`. Confirm the child editor comes from `Karpik.Editor\bin\Debug\net10.0`, and its `KarpikEngineRoot` points to the selected SDK installation.

## Validation and Acceptance

Acceptance requires all of the following:

- Resolver tests show Debug source editor selection without changing the SDK installation root.
- Resolver tests reject a missing Debug source editor instead of falling back.
- Release/no-debug tests continue to select the SDK editor.
- Process-host tests show the child working directory equals `EditorLaunchDescriptor.WorkingDirectory`.
- `dotnet test Karpik.Launcher.Tests\Karpik.Launcher.Tests.csproj --no-restore -m:1 -nr:false` exits zero.
- `dotnet build Karpik.Launcher\Karpik.Launcher.csproj --no-restore -m:1 -nr:false` exits zero.
- A manual Debug launch starts the source editor and an external game build continues to use its exact SDK runtime.

## Idempotence and Recovery

The tests and Debug builds are safe to rerun. If a Debug editor output is stale or missing, build `Karpik.Editor/Karpik.Editor.csproj` in Debug, then rerun launcher; the launcher must not use an SDK fallback. Reverting the four implementation files and the Debug metadata returns the previous SDK-only editor behavior. This plan does not publish, delete, move, or modify installed SDK payloads.

## Artifacts and Notes

- Design authority: `docs/02_ADR/versioned-engine-sdk-and-external-game-projects.md`.
- Active plan: `plans/debug-source-editor-launch-execplan.md`.
- No ADR update is planned: the versioned SDK contract remains intact; this is a Debug-only development-host override.
