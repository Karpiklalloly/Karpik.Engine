# Editor structured log history Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show only structured `ILogger` events in the Editor console, filter them by level and session, and archive all accepted events to JSONL files.

**Architecture:** Editor workers retain `AddSimpleConsole()` and, only when launched by the Editor, register a second `AddEditorConsole()` provider. The provider writes a marked JSON line; Core owns that small protocol, while Editor owns filtering and the asynchronous archive writer.

**Tech Stack:** .NET 10, Microsoft.Extensions.Logging, System.Text.Json, System.Threading.Channels, Avalonia, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-18-editor-structured-log-history-design.md`

## Global Constraints

- No new NuGet dependencies.
- Retain `AddSimpleConsole()`; add `AddEditorConsole()` only when `KARPIK_EDITOR_LOG_CAPTURE=1`.
- The exact wire prefix is `@karpik-editor-log:` and unmarked or malformed lines never reach the console model.
- The wire payload has UTC timestamp, numeric level, and message. JSON escaping guarantees one physical line per event.
- Keep the current Debug minimum level. Do not enable Trace globally.
- Memory keeps the latest 2,000 accepted records; disk keeps all queued records in files under `%LocalAppData%\KarpikEngine\Editor\logs` and prunes oldest completed archives above 1 GiB.
- Runtime output backpressure must not block the Avalonia UI thread; edited code must not enter a tick, ECS Run, render, network, or serialization hot path.
- Protocol belongs in `Karpik.Engine.Core`, the provider in Shared LoggerModule, and UI/archive code in `Karpik.Editor`.

---

This ExecPlan is a living document and must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

The Editor console displays a single row per `ILogger` call. Changing either selector modifies only visibility: hidden Debug messages and records from stopped sessions remain in the in-memory and disk history. The user can inspect the active run's JSONL archive after closing the Editor.

## Progress

- [ ] (2026-09-18) Initial plan created.
- [ ] Protocol and editor-worker environment flag tested.
- [ ] Opt-in logger provider tested.
- [ ] Editor history and archive tested.
- [ ] Console UI binding tested and validated.

## Surprises & Discoveries

- Observation: LoggerModule currently configures only `AddSimpleConsole()` at Debug.
  Evidence: `Modules/Shared/LoggerModule/LoggerModuleInstaller.cs`.
- Observation: Editor currently queues raw stdout/stderr strings and bounds the UI collection to 2,000 entries.
  Evidence: `Karpik.Editor/ViewModels/EditorShellViewModel.cs`.

## Decision Log

- Decision: Use a second, opt-in, marked JSON provider beside SimpleConsole.
  Rationale: Prefix parsing of SimpleConsole permits arbitrary Console.WriteLine text to impersonate a log; marked output does not.
  Date/Author: 2026-09-18 / Codex and developer.
- Decision: Persist in Editor rather than the worker provider.
  Rationale: Worker-side disk I/O could delay runtime log callers; the Editor can serialize it off UI.
  Date/Author: 2026-09-18 / Codex and developer.

## Outcomes & Retrospective

No outcome yet. Update after validation.

## Context and Orientation

`LoggerModuleInstaller` builds the worker factory. `ProcessManager.CreateStartInfo` creates worker environments. `EditorPreviewController` enables output capture. `EditorSessionManager` attaches a session to each captured line. `EditorShellViewModel` currently queues raw output and `MainWindow.axaml` renders a string list. JSONL is one JSON object per line and supports append-only recovery.

## Real-Time Assessment

No game loop or ECS path changes. The custom provider allocates only for enabled log events, as the existing console provider does. A bounded archive channel may apply backpressure to the redirected-output handler, never to the UI thread, and avoids an unbounded queue. Client/Server dependencies do not change.

## Plan of Work

First add a Core protocol with integer log levels and a `CaptureEditorLogs` HotReload option. ProcessManager sets the environment flag and EditorPreviewController opts in. Then add the conditional LoggerModule provider. Finally replace string-only console history with structured entries, add the archive writer and selectors, and parse output before UI dispatch.

## Milestones

1. Protocol round-trip, malformed input, single-line escaping, and start-info environment flag pass targeted tests.
2. AddEditorConsole emits exactly one marked line only with the environment flag.
3. Filtering retains hidden history; JSONL persistence, 2,000-item eviction, and 1 GiB pruning pass unit tests.
4. Shell ignores raw stdout/stderr and binds the two selectors; all Editor tests and build pass.

## Concrete Steps

All commands use `C:\Users\artem\RiderProjects\KarpikEngine`.

### Task 1: Core protocol and process opt-in

**Files:** Create `Karpik.Engine.Core/Logging/EditorConsoleLogProtocol.cs` and `Karpik.Editor.Tests/EditorConsoleLogProtocolTests.cs`; modify `Karpik.Engine.Core/HotReloadOptions.cs`, `Karpik.Engine.Core/ProcessManagement/ProcessManager.cs`, `Karpik.Engine.Core/Editor/EditorPreviewController.cs`, and `Karpik.Engine.Core.Runner.Tests/ProcessManagerLifecycleTests.cs`.

**Interfaces:** `EditorConsoleLogProtocol.Serialize(DateTimeOffset, int, string): string`; `TryParse(string, out EditorConsoleLogEvent?): bool`; `HotReloadOptions.CaptureEditorLogs: bool`.

- [ ] Write failing tests: a multiline message round-trips as one prefixed physical line; malformed JSON is rejected; `CreateStartInfo` sets `KARPIK_EDITOR_LOG_CAPTURE=1` only when `CaptureEditorLogs` is true.
- [ ] Run `dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --no-restore --filter EditorConsoleLogProtocolTests` and `dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --no-restore --filter ProcessManagerLifecycleTests`; expect missing API or a failed assertion.
- [ ] Implement a JSON record `(Timestamp, Level, Message)`, exact-prefix parsing, the HotReload option, environment assignment, and EditorPreviewController opt-in in both constructors.
- [ ] Repeat both test commands; expect passing results. Commit only Task 1 files as `feat: add editor log protocol`.

### Task 2: AddEditorConsole

**Files:** Create `Modules/Shared/LoggerModule/EditorConsoleLoggerExtensions.cs` and `Karpik.Editor.Tests/EditorConsoleLoggerTests.cs`; modify `Modules/Shared/LoggerModule/LoggerModuleInstaller.cs` and only add a test-project reference if compilation requires it.

**Interfaces:** `ILoggingBuilder AddEditorConsole(this ILoggingBuilder builder)` consumes `EditorConsoleLogProtocol.Serialize`.

- [ ] Write failing tests that redirect Console.Out, set the flag, call `ILogger.LogInformation` with a newline, and assert one parseable marker line; with the flag absent, assert no marker. Restore both Console.Out and environment in finally.
- [ ] Run `dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --no-restore --filter EditorConsoleLoggerTests`; expect failure.
- [ ] Implement one disposable ILoggerProvider with a no-op scope. Its enabled logger invokes the formatter, maps `LogLevel` to an integer, and writes the Core protocol line. Retain AddSimpleConsole and conditionally call AddEditorConsole in LoggerModuleInstaller.
- [ ] Repeat the logger test; expect passing result. Commit only Task 2 files as `feat: add editor console logger`.

### Task 3: Structured history and archive

**Files:** Create `Karpik.Editor/Models/EditorConsoleLogEntry.cs`, `Karpik.Editor/Models/EditorLogArchive.cs`, and `Karpik.Editor.Tests/EditorConsoleHistoryTests.cs`; modify `Karpik.Editor/ViewModels/EditorShellViewModel.cs`.

**Interfaces:** `ConsoleViewModel.Add(EditorConsoleLogEntry)`, `RegisterSession(string)`, `MinimumLevel`, `SelectedSession`, `VisibleEntries`, and `IAsyncDisposable EditorLogArchive`.

- [ ] Write failing tests: Debug stays in all history after Warning filter; stopped session stays selectable; multiline text renders as one row; archive JSONL round-trips two records; 2,001 entries evict only the oldest memory item; pruning leaves newest completed files under a supplied cap.
- [ ] Run `dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --no-restore --filter EditorConsoleHistoryTests`; expect missing API.
- [ ] Implement the 2,000-item all-history queue and derived visible list. Use a bounded Channel with FullMode.Wait and an archive writer that uses a timestamp-plus-guid JSONL filename, writes session-enriched records, flushes on DisposeAsync, and deletes oldest completed files above 1 GiB. Report archive failure once through a status callback, never through the log stream.
- [ ] Repeat the history test; expect passing result. Commit only Task 3 files as `feat: archive editor logs`.

### Task 4: Shell and Avalonia binding

**Files:** Modify `Karpik.Editor/ViewModels/EditorShellViewModel.cs` and `Karpik.Editor/MainWindow.axaml`; create `Karpik.Editor.Tests/EditorShellLogTests.cs` if the behavior does not fit existing editor-shell tests.

**Interfaces:** consume `EditorConsoleLogProtocol.TryParse`, ConsoleViewModel, and EditorLogArchive.

- [ ] Write a failing fake-session test with ordinary stdout, stderr, malformed marker JSON, and valid server/client records. Assert only valid entries appear, then filter by server and Warning without mutating all history.
- [ ] Run `dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --no-restore --filter EditorShellLogTests`; expect raw-output behavior failure.
- [ ] Register every new session; parse before UI dispatch; ignore invalid lines; await archive enqueue outside Dispatcher; dispatch only model mutation; dispose the archive on shell shutdown. Remove direct build, publish, lifecycle, and project diagnostic additions to Console. Bind level and session ComboBoxes and bind the ListBox to visible structured rows as `[session] [level] message` with no wrapping.
- [ ] Run `dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --no-restore`, `dotnet build Karpik.Editor\Karpik.Editor.csproj -m:1 -nr:false --no-restore`, and `git diff --check`; expect zero failures and a successful build. Commit only Task 4 files as `feat: filter editor runtime logs`.

## Validation and Acceptance

Run the two targeted Task 1 commands, then the full Task 4 tests and build. Manual acceptance: start server plus two clients, emit Information and Warning logger messages, prove raw stdout/stderr is absent, verify a multiline message occupies one row, change both selectors, stop a client and filter its prior records, then inspect the current JSONL archive for all levels including hidden Debug records.

## Idempotence and Recovery

Tests and builds are repeatable. A failed archive write leaves the in-memory console working and reports one status diagnostic. A crash can leave only the final JSONL line incomplete. Reverting removes the provider registration, Editor archive/model files, and process flag while leaving AddSimpleConsole intact; never reset unrelated changes.

## Artifacts and Notes

- Design: `docs/superpowers/specs/2026-09-18-editor-structured-log-history-design.md`.
- `docs/modules/shared/logger.md` is already stale and is not rewritten in this feature.
- No ADR is needed because this is an Editor-local observability contract.
