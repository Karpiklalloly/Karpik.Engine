# Open editor projects before runtime activation

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Opening a valid Karpik `.slnx` must immediately make the desktop editor useful: the project path, Content tree, and build/publish commands become available even when MSBuild runtime evaluation is slow or fails. Server/client launch remains unavailable until a separate runtime activation step evaluates and validates the external game's runtime descriptors. A failed activation reports its diagnostics without closing the project.

## Progress

- [x] (2026-09-13) Initial plan created; user approved deferred runtime activation.
- [x] (2026-09-13) Add project-open state that does not require a ready runtime descriptor.
- [x] (2026-09-13) Defer runtime evaluation until an explicit runtime check.
- [x] (2026-09-13) Update editor command state and diagnostics.
- [x] (2026-09-13) Add focused regression test and run `Karpik.Editor.Tests` (94 passed, 8 skipped).

## Surprises & Discoveries

- Observation: The editor currently runs `dotnet msbuild ... -getProperty ... -getItem:ProjectReference` for every declared project while opening a solution, with a 30-second per-project deadline.
  Evidence: `Karpik.Editor/Projects/MsBuildProjectInspector.cs`, `ProjectOpenService.OpenAsync`.
- Observation: Manual evaluation of both reported MyFirstGame3 client projects completed in approximately 3.6 seconds after the failure, so the timeout is intermittent and has affected more than one project.
  Evidence: 2026-09-13 local diagnostic runs against `MyFirstGame3.Client.Launcher.csproj` and `MyFirstGame3.Client.csproj`.
- Observation: Reusing the existing project-switch path for an explicit runtime check preserves its generation, cancellation, and cleanup guarantees without adding a second evaluator lifecycle.
  Evidence: `ProjectSwitchCoordinator.SwitchAsync` now receives the explicit evaluation flag.

## Decision Log

- Decision: Decouple project opening from runtime evaluation; retain runtime validation at the server/client launch boundary.
  Rationale: Build and asset authoring require a valid solution, not runtime bundles or runners. Launch requires the descriptor and must remain blocked until its validation succeeds.
  Date/Author: 2026-09-13 / developer and AI assistant
- Decision: Keep build and publish available for an opened solution. Require runtime activation only for `Start server` and `Add client`.
  Rationale: Their command inputs come from the validated solution declaration; session creation is the only operation that consumes runtime descriptor paths.
  Date/Author: 2026-09-13 / developer and AI assistant
- Decision: Implement explicit activation as a re-evaluation of the current solution through the existing project-switch transaction, rather than a new background service.
  Rationale: The coordinator already serializes switches, cancels active work, and rejects stale generations. A separate lifecycle would duplicate those failure-prone responsibilities.
  Date/Author: 2026-09-13 / developer and AI assistant

## Outcomes & Retrospective

Primary outcome: opening an external project no longer starts MSBuild evaluation. The editor publishes an unactivated project with Build/Publish enabled and Server/Client launch disabled. The new `Проверить runtime` command reruns the established evaluation path; a failure leaves the project open and records the diagnostic.

## Context and Orientation

`Karpik.Editor/Projects/ProjectOpenService.cs` currently validates the solution, resolves an engine installation, evaluates every project through `IMsBuildProjectInspector`, validates the resulting `ProjectRuntimeDescriptor`, then creates `ActiveProjectContext`. Any evaluation failure returns `ProjectOpenResult.Failure`, causing `ProjectSwitchCoordinator` to clear the active project.

`Karpik.Editor/Projects/ActiveProjectContext.cs` requires a non-null runtime descriptor. `EditorProjectLifetime` creates both an `EditorProjectCommandRunner` (used for build/publish) and an `EditorSessionManager` (used for preview launch) in its constructor. This couples commands that need only a solution to runtime evaluation.

`Karpik.Editor/ViewModels/EditorShellViewModel.cs` publishes the active context and derives command enablement from the session manager and project path. `Karpik.Editor/MainWindow.axaml` already binds those commands and status fields. The project/asset browser is only populated after publication, so opening currently cannot reach it if evaluation times out.

## Real-Time Assessment

This changes only the Avalonia editor orchestration process and external MSBuild child-process lifecycle. It does not run in `Update`, `FixedUpdate`, ECS systems, rendering, serialization, networking, or any Client/Server gameplay module. File I/O and allocations remain outside engine hot paths. No Client/Server project references are added. Existing command gates and cancellation remain the concurrency boundary; activation must use the same project generation checks as build/publish so a result from a stale project is discarded.

## Plan of Work

1. Represent an opened project separately from an activated runtime. Make the runtime descriptor and session manager optional in `ActiveProjectContext`/`EditorProjectLifetime`, while retaining the solution-scoped command runner and cleanup behavior.
2. Change `ProjectOpenService.OpenAsync` to publish a context after structural solution validation and engine-installation resolution. Preserve evaluation diagnostics on the context rather than converting them into a failed project open.
3. Add an activation operation that executes the existing evaluation and `ValidateEvaluations` flow for the currently active generation. On success it installs a fresh runtime descriptor/session manager. On failure it leaves the context open and records the full bounded MSBuild standard output/error and timeout diagnostic.
4. Make `EditorShellViewModel` expose activation state and a `CheckRuntimeCommand`. Keep Build/Publish enabled for an opened context; disable Start server/Add client until activation succeeds. Do not clear Project/asset state when activation fails.
5. Preserve switch/shutdown ordering: cancel any active command or activation, stop existing sessions, dispose the previous project context, and reject stale activation completion before it mutates the shell.

## Milestones

### 1. Open without runtime activation

Update project-open/context types and tests so a structurally valid solution can produce and publish an active project when the inspector fails. Verify that the project path and asset browser survive the diagnostic.

Run from `C:\Users\artem\RiderProjects\KarpikEngine`:

`dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj --no-restore -m:1 -nr:false --filter FullyQualifiedName~ProjectOpenServiceTests`

Expected: project-open tests pass, including the newly added nonfatal-evaluation scenario.

### 2. Activate runtime on demand

Add the activation operation and tests for success, timeout/failure retention, and stale-result rejection during a project switch.

Run from `C:\Users\artem\RiderProjects\KarpikEngine`:

`dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj --no-restore -m:1 -nr:false --filter FullyQualifiedName~RuntimeActivation`

Expected: launch prerequisites are created only after successful activation; failures retain the open project.

### 3. Command state and diagnostics

Bind the activation state/command in the shell and UI. Verify build runs from an unactivated project, launch buttons stay disabled until activation, and console output retains both MSBuild streams up to their existing bounds.

Run from `C:\Users\artem\RiderProjects\KarpikEngine`:

`dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj --no-restore -m:1 -nr:false`

Expected: all editor tests pass; existing platform-dependent skips remain documented by the test output.

## Concrete Steps

1. Add tests before each production change in `Karpik.Editor.Tests/Projects/` and editor-shell tests.
2. Refactor `ActiveProjectContext`, `EditorProjectLifetime`, and their factories so command execution exists before runtime activation.
3. Extract the existing evaluation/descriptor construction from `ProjectOpenService.OpenAsync` into an activation path scoped to `ProjectGeneration`.
4. Update `ProjectSwitchCoordinator` and `EditorShellViewModel` to publish the basic context, then invoke activation only from explicit launch/check commands.
5. Update `MainWindow.axaml` with a non-blocking runtime status and retry/check action.
6. Run the milestone tests and the full editor test project after each milestone; record results in Progress.

## Validation and Acceptance

- Opening a valid external project while its evaluator times out leaves the project visible and Content tree populated.
- `Build` and `Publish` remain available for that opened project.
- `Start server` and `Add client` remain disabled with a visible diagnostic until evaluation succeeds.
- An explicit runtime check can succeed later without reopening the solution and then enables launch.
- Switching projects or shutting down while evaluation is in progress cancels it and cannot apply a stale descriptor.
- `dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj --no-restore -m:1 -nr:false` exits with zero failures.

## Idempotence and Recovery

Runtime activation is retryable: each run either replaces the current validated descriptor only after complete success or leaves the previous/open-only state intact. Project switching/shutdown cancels in-flight work through the existing command gates. If a regression is found, revert the editor-only commit; no game project files, bundle formats, or Client/Server runtime assemblies are changed.

## Artifacts and Notes

- `Karpik.Editor/Projects/MsBuildProjectInspector.cs` keeps the bounded 30-second evaluation and process-tree cleanup; this work changes its failure's scope, not the timeout itself.
- The decision may warrant an ADR after implementation if deferred runtime activation becomes a stable editor lifecycle boundary.
