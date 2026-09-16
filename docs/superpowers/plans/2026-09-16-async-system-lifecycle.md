# Async System Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Await opt-in asynchronous system startup before a worker enters its frame loop and asynchronous system cleanup before it disposes the runtime.

**Architecture:** Dragon ECS stays synchronous. Karpik's `Builder` records async lifecycle systems while it builds the pipeline. `EngineRunner` awaits the lists at the host boundary: registration order on startup and reverse order on shutdown.

**Tech Stack:** .NET `ValueTask`, Autofac scopes, Dragon ECS pipeline, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-16-async-system-lifecycle-design.md`

## Global Constraints

- Async lifecycle is startup/shutdown only; frame phases receive no await, I/O, locks, or allocations.
- `ISystemInit` and `ISystemDestroy` retain their synchronous signatures and Dragon adapters.
- Hosts may wait at setup/shutdown only when needed to retain main-thread affinity; frame phases must never wait.
- Initialization is registration order; destruction is reverse order.
- Async destruction continues after individual failures, then reports aggregate failures.

---

### Task 1: Define and collect the asynchronous contracts

**Files:**
- Create: `Karpik.Engine.Core/LifeCycle/ISystemAsyncInit.cs`
- Create: `Karpik.Engine.Core/LifeCycle/ISystemAsyncDestroy.cs`
- Modify: `Karpik.Engine.Core.Runner/Builder.cs`
- Test: `Karpik.Engine.Core.Runner.Tests/Program.cs`

**Interfaces:**
- `ValueTask ISystemAsyncInit.InitAsync(CancellationToken cancellationToken)`
- `ValueTask ISystemAsyncDestroy.DestroyAsync()`
- Ordered `Builder` initializer/destroyer lists populated by `Add(object, ...)`, never by Dragon adapters.

- [x] **Step 1: Write the failing order test**

Add `AsyncLifecycleSmokeSystem`, implementing `ISystemInit`, `ISystemAsyncInit`, `ISystemAsyncDestroy`, and `ISystemDestroy`, to the runner smoke module. Append `Init`, `AsyncInit`, `AsyncDestroy`, and `Destroy` to `LifecycleTrace`, then assert:

~~~csharp
AssertSequence(["Init", "AsyncInit", "AsyncDestroy", "Destroy"], LifecycleTrace.Items);
~~~

- [x] **Step 2: Verify RED**

Run in `C:\Users\artem\RiderProjects\KarpikEngine`:

~~~powershell
dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --filter FullyQualifiedName~AsyncLifecycle
~~~

Expected: compilation fails because contracts and async runner APIs are absent.

- [x] **Step 3: Add contracts and collection**

Create the two contracts with the stated signatures. In `Builder.Add(object, ...)`, append matching async interfaces to ordered lists. Do not execute them and do not wrap them in `IEcsInit` or `IEcsDestroy`.

- [x] **Step 4: Re-run the focused test**

Run the Step 2 command. Expected: it progresses beyond missing contract errors; execution still fails until Task 2.

### Task 2: Await async lifecycle in `EngineRunner`

**Files:**
- Modify: `Karpik.Engine.Core/IEngineRunner.cs`
- Modify: `Karpik.Engine.Core/MainThreadScheduler.cs`
- Modify: `Karpik.Engine.Core.Runner/Runner.cs`
- Test: `Karpik.Engine.Core.Runner.Tests/Program.cs`
- Test: `Karpik.Engine.Core.Runner.Tests/GameplayLoopDriverTests.cs`

**Interfaces:**
- `Task IEngineRunner.SetupAsync(Application, MainThreadScheduler, ClientFrameMetrics, Dictionary<string, byte[]>?)`
- `Task IEngineRunner.DestroyAsync()`
- `Task MainThreadScheduler.ScheduleAsync(Func<Task>)`

- [x] **Step 1: Extend the RED test for incomplete initialization**

Make `AsyncLifecycleSmokeSystem.InitAsync` await a test-owned `TaskCompletionSource`. After executing the scheduler, assert setup is incomplete. Complete the source, await setup, and assert `AsyncInit` precedes the first frame phase.

- [x] **Step 2: Verify RED**

Run the Task 1 test command. Expected: compile failure because `SetupAsync` is absent.

- [x] **Step 3: Implement setup scheduling**

Add `ScheduleAsync(Func<Task>)` with a `TaskCompletionSource` created using `RunContinuationsAsynchronously`. The queue action invokes the callback and a private async completion method forwards result, cancellation, or exception; it must never use `async void`.

Move scheduled setup into `EngineRunner.SetupCoreAsync`. Call `_pipeline.Init()`, then await collected initializers in list order. Retain destroyers for shutdown. On startup failure, await cleanup and fault the setup task.

- [x] **Step 4: Implement shutdown ordering**

Before `pipeline.Destroy()` in `DestroyAsyncCore`, invoke saved destroyers last-to-first. Record every exception, continue cleanup, then include those failures in the existing `AggregateException`. Keep synchronous pipeline destroy after async destruction.

- [x] **Step 5: Verify GREEN**

~~~powershell
dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~AsyncLifecycle|FullyQualifiedName~EngineRunnerLifecycleTests|FullyQualifiedName~GameplayLoopDriverTests"
~~~

Expected: selected async and existing synchronous lifecycle tests pass.

### Task 3: Await lifecycle boundaries in bootstrap and worker hosts

**Files:**
- Modify: `Karpik.Engine.Core/Bootstrap.cs`
- Modify: `Karpik.Engine.Core/CoreRunner.cs`
- Modify: `Karpik.Engine.Core.Runner/WorkerHost.cs`
- Modify: `Karpik.Engine.Core.Runner/Program.cs`
- Modify: `Karpik.Engine.Core.Runner/StaticComposition/StaticEngineHost.cs`
- Test: `Karpik.Engine.Core.Runner.Tests/BootstrapBoundaryTests.cs`

**Interfaces:**
- Bootstrap retains the `Task` returned by `IEngineRunner.SetupAsync` and exposes it for host awaiting.
- `Bootstrap.ShutdownAsync()` returns `IEngineRunner.DestroyAsync()`.
- `WorkerHost.RunAsync(...)` is awaited by dynamic and static entry points.

- [x] **Step 1: Write the failing bootstrap test**

Create a fake `IEngineRunner` that records async setup and destruction. Start `Bootstrap`, execute its scheduler, await startup, then await shutdown. Assert setup ends before a loop call and shutdown waits for destroy.

- [x] **Step 2: Verify RED**

~~~powershell
dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --filter FullyQualifiedName~Bootstrap
~~~

Expected: compilation fails because Bootstrap has no awaitable startup/shutdown boundary.

- [x] **Step 3: Propagate async host APIs**

Have `Bootstrap.Initialize` start `SetupAsync` and retain its task. Worker startup executes the main-thread queue, then completes startup before ready IPC and any frame loop. Keep the worker's original main thread at this boundary (a console-host `await` may resume on the thread pool). Convert worker startup/teardown to `RunAsync`, dynamic `Program.Main` to `async Task Main`, and static `RunAsync` to await the worker. Update legacy `CoreRunner` only outside its frame loops.

- [x] **Step 4: Verify GREEN**

~~~powershell
dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~WorkerHost|FullyQualifiedName~StaticComposition"
~~~

Expected: bootstrap, dynamic-host, and static-host tests pass.

### Task 4: Document and validate

**Files:**
- Modify: `docs/04_Roadmap/0.4-system-lifecycle-plan.md`
- Modify: `docs/superpowers/specs/2026-09-16-async-system-lifecycle-design.md` only if implementation reveals a contract change.

- [x] **Step 1: Document the implemented lifecycle**

Add the two async contracts to the lifecycle list and state that they execute only at setup/shutdown boundaries.

- [x] **Step 2: Build the smallest affected project**

~~~powershell
dotnet build Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj -m:1 -nr:false
~~~

Observed: `Karpik.Engine.Core.Runner.Tests` (which references the runner) builds with exit code 0.

- [ ] **Step 3: Run the full runner test project**

~~~powershell
dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
~~~

Observed: the full run fails in this environment with named-pipe access and timeout errors in process/IPC tests. Focused lifecycle, bootstrap, static-composition, and runner tests pass.

- [x] **Step 4: Check the diff**

~~~powershell
git diff --check
git status --short
~~~

Expected: no whitespace errors; the pre-existing untracked `test/` directory is untouched.

## Validation and Acceptance

- Async initialization delays host readiness and every frame until completion.
- Async initializers run in registration order; destroyers run in reverse order before synchronous destroy and scope disposal.
- Every async cleanup runs despite another cleanup failure, and failures are aggregated.
- Existing synchronous lifecycle, static composition, and worker startup tests remain green.
- No frame phase receives await, I/O, locking, or per-frame allocation.

## Idempotence and Recovery

Commands are safe to rerun. On setup failure, `EngineRunner` cleans up its partially created runtime. If startup deadlocks, inspect `MainThreadScheduler.ScheduleAsync`; no code may synchronously wait for a task requiring that scheduler.

## Artifacts and Notes

- Design: `docs/superpowers/specs/2026-09-16-async-system-lifecycle-design.md`.
- The repository has an unrelated untracked `test/` directory. Do not add, modify, or remove it.
