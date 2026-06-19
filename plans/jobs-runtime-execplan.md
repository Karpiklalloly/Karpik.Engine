# Stabilize the standalone no-GC jobs runtime

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Replace the managed-allocation-heavy short-job path in `Karpik.Jobs` with a standalone no-GC runtime. The result schedules value-type jobs from one orchestration thread, executes them on worker threads through bounded native descriptors and work-stealing deques, and exposes measured overflow diagnostics.

## Progress

- [x] (2026-06-03 00:33 +04:00) Design agreed as child plan 2 of `plans/scheduler-jobs-memory-execplan.md`.
- [x] (2026-06-04 16:04 +04:00) Measured current delegate-based jobs managed allocation, throughput, and round-trip tail latency in Release with `Karpik.Jobs.Benchmarks`.
- [x] (2026-06-03 22:36 +04:00) Added focused baseline correctness tests for the current delegate-based `JobSystem`: independent jobs execute exactly once, dependency chains preserve order, fan-out/fan-in complete before dependent work, `EnqueueParallel` covers every index once, exceptions propagate through awaiters, and shutdown rejects new publication with a completed default handle.
- [x] (2026-06-04 16:04 +04:00) Added lightweight jobs benchmark project covering independent jobs, dependency chains, parallel batches, and p50/p95/p99 completion latency at 1/2/4/8 workers.
- [x] (2026-06-04 16:25 +04:00) Added public `IJob`/`IJobFor` value-job contracts and internal preallocated native descriptor storage with stale-handle invalidation and 0 B steady-state managed allocation test.
- [x] (2026-06-04 16:44 +04:00) Added `JobScheduler` value scheduling entry points backed by native descriptor and payload storage: `TrySchedule`, `TryScheduleParallel`, `Schedule`, `ScheduleParallel`, and `Complete`.
- [x] (2026-06-04 17:02 +04:00) Added value-job dependency storage, pending completion checks, completed/failed generation tracking, and cold-path exception reporting.
- [x] (2026-06-04 17:22 +04:00) Added optional value-job profiler hooks and explicit parallel batch publication metadata through `JobProfilerEvent` and `JobBatchInfo`.
- [x] (2026-06-04 17:42 +04:00) Added standalone bounded `WorkStealingDeque<T>` backed by native storage with owner bottom push/pop, thief top steal, wrap-around tests, and 0 B steady-state allocation test.
- [x] (2026-06-04 19:31 +04:00) Wired bounded `WorkStealingDeque<ValueJobHandle>` instances into value-job worker publication through `TryPublish`, deterministic `TryRunNext`, local owner pop, cross-worker steal, pending dependency requeue, capacity failure, and 0 B steady-state publish/run allocation tests.
- [x] (2026-06-04 19:36 +04:00) Added one-shot value-job worker runtime startup/shutdown, background worker loops, wake-up on publish/completion, steal-only worker drain for externally published queues, stopped-publication rejection, volatile completion visibility, and 0 B orchestration-thread schedule/publish/wait test.
- [x] (2026-06-04 19:44 +04:00) Kept legacy delegate `JobSystem` isolated from `JobScheduler` with reflection tests proving the value scheduler does not reference `JobSystem`, `JobHandle`, `JobWrapper`, `JobCompletion`, `CancellationTokenSource`, or `ConcurrentQueue<JobWrapper>`.
- [x] (2026-06-04 19:44 +04:00) Marked delegate `JobSystem`, constructor, `Enqueue`, `EnqueueParallel`, and `Combine` APIs with `AllocatingCompatibilityAttribute` so managed-allocation compatibility paths are machine-checkable.
- [x] (2026-06-04 20:17 +04:00) Ran standalone acceptance gate: clean `Karpik.Jobs` build, Debug/Release focused tests, benchmark project build, delegate baseline benchmark, and value scheduler benchmark with `0 B` managed allocation in measured value paths.

## Surprises & Discoveries

- Observation: Current scheduling allocates one `CancellationTokenSource` and one `JobCompletion` per short job, plus closures for enqueue, dependencies, parallel batches, and typed jobs.
  Evidence: `Karpik.Jobs/JobSystem.cs`.

- Observation: Current worker queues are `ConcurrentQueue<JobWrapper>` and stealing samples one random victim.
  Evidence: `JobSystem.ThreadState`, `TryPopTask`, and `TryStealTask`.

- Observation: Current completion stores continuations in multicast delegates and owns `ManualResetEventSlim`.
  Evidence: `Karpik.Jobs/JobCompletion.cs`.

- Observation: Current delegate-based independent short jobs allocate about 280-291 B of managed memory per publication on the orchestration thread after warm-up, even when the user job delegate is cached.
  Evidence: `dotnet run --project Karpik.Jobs.Benchmarks\Karpik.Jobs.Benchmarks.csproj -c Release --no-build` on 2026-06-04 measured 2,800,000-2,915,456 B for 10,000 independent jobs across 1/2/4/8 workers.

- Observation: Current dependency-chain publication allocates about 512 B per chained job after warm-up because every dependency path creates completion and continuation state.
  Evidence: `Karpik.Jobs.Benchmarks` measured 511,792-511,856 B for 1,000 chain jobs across 1/2/4/8 workers.

- Observation: Current-thread allocation measurements do not include allocations performed by worker threads.
  Evidence: Benchmark intentionally uses `GC.GetAllocatedBytesForCurrentThread()` to isolate orchestration-thread publication cost.

- Observation: Adding more workers does not monotonically improve current delegate-runtime throughput for tiny jobs; 8-worker independent throughput was lower than 2-worker throughput in the latest baseline run.
  Evidence: Latest baseline measured about 1,010,581 jobs/s at 2 workers and about 580,659 jobs/s at 8 workers.

- Observation: `NativeArray<T>` storage is not implicitly zeroed by the API contract.
  Evidence: The initial `IJobFor` contract test incremented uninitialized native integers and failed until the test explicitly called `NativeArray<int>.Clear()`.

- Observation: The no-GC value scheduling path must not reuse the existing public `JobHandle`.
  Evidence: Existing `JobHandle` owns managed `JobCompletion` and optional `CancellationTokenSource`; the new `JobScheduler` returns `ValueJobHandle` over native descriptor identity instead.

- Observation: `NativeResult<T>` storage is not implicitly zeroed by the API contract.
  Evidence: Dependency test initially compared an uninitialized `NativeResult<int>` value to zero before any job executed; the test now writes the initial value explicitly.

- Observation: Completed dependency handles must remain meaningful after their descriptor slot is returned and reused.
  Evidence: `JobScheduler` tracks completed generations per descriptor slot, so a job can depend on an already-completed handle without keeping the old slot rented.

- Observation: Profiler hooks must be opt-in and checked before timestamp capture.
  Evidence: `JobScheduler` only calls `Stopwatch.GetTimestamp()` while creating `JobProfilerEvent` after a non-null callback is found; Release tests keep no-hook schedule/complete at 0 B managed allocation.

- Observation: A power-of-two bounded deque keeps circular indexing cheap and avoids dynamic growth policy in the scheduler hot path.
  Evidence: `WorkStealingDeque<T>` rejects non-power-of-two capacity and indexes native storage with `index & (capacity - 1)`.

- Observation: Value-job worker publication is deterministic/manual, not a background worker loop yet.
  Evidence: `JobScheduler.TryPublish` pushes a `ValueJobHandle` into a configured worker deque, and `JobScheduler.TryRunNext` drains or steals one handle per call.

- Observation: A stolen job whose dependencies are still pending must return to the victim queue, not the thief's local queue.
  Evidence: `JobSchedulerWorkerPublicationTests.TryRunNext_WhenStolenDependencyIsPending_RequeuesToVictimWorker` uses queue capacity 1 and verifies the pending handle remains on the original worker until its dependency completes.

- Observation: Real worker loops cannot call owner `TryPopBottom` on queues filled by the orchestration thread.
  Evidence: `JobScheduler.RunWorkerLoop` drains published work through `TryRunNextPublished`, which uses `TryStealTop` even for the worker's assigned queue; manual `TryRunNext` returns `false` while workers are running.

- Observation: `IsCompleted` can become visible before descriptor reuse is observed by a subsequent `TrySchedule` on a capacity-1 scheduler.
  Evidence: Worker runtime tests wait for both `IsCompleted(handle)` and `ScheduledCount == 0` when immediately reusing a single descriptor slot.

- Observation: The wake-up path needs a lost-signal guard after resetting the shared event.
  Evidence: `JobScheduler.RunWorkerLoop` rechecks `HasQueuedWorkerWork()` after `_workerWakeEvent.Reset()` before waiting.

- Observation: Legacy delegate publication remains intentionally managed-allocation-heavy and isolated from the no-GC value scheduler.
  Evidence: `JobSystemCompatibilityBoundaryTests.ValueJobScheduler_DoesNotReferenceLegacyDelegateCompatibilityTypes` rejects references from `JobScheduler` to legacy delegate runtime types.

- Observation: Value scheduler measured `0 B` managed allocation after warm-up for schedule/complete, publish/run-next, worker-runtime publish/wait, and parallel batch paths.
  Evidence: `dotnet run --project Karpik.Jobs.Benchmarks\Karpik.Jobs.Benchmarks.csproj -c Release --no-build` on 2026-06-04 measured `managedBytes=0` for `value-schedule-complete`, `value-publish-run-next`, `value-worker-runtime`, and `value-parallel-batch`.

- Observation: Fresh delegate baseline still allocates managed memory and remains compatibility-only.
  Evidence: The same benchmark run measured about `2,800,000-2,915,456 B` for 10,000 independent delegate jobs, about `511,792-511,856 B` for 1,000 dependency-chain jobs, and about `59,728-65,872 B` for delegate parallel batches depending on worker count.

## Decision Log

- Decision: New hot-path jobs are `struct` payloads implementing `IJob` or `IJobFor`.
  Rationale: Copyable value payloads can live in native descriptors without closures or managed wrapper objects.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Descriptor exhaustion uses a non-throwing `TryRent` result for the preallocated descriptor pool.
  Rationale: Exhaustion must be predictable and observable in scheduler hot paths; exceptions are reserved for invalid debug/safety access such as stale handle `Get`.
  Date/Author: 2026-06-04 / agent

- Decision: Initial value scheduling executes through `Complete(ValueJobHandle)` on the orchestration thread and does not yet publish to worker queues.
  Rationale: Milestone 2 validates API shape, native payload copy, descriptor lifetime, stale-handle safety, and 0 B schedule/complete before adding dependencies and work stealing.
  Date/Author: 2026-06-04 / agent

- Decision: Value scheduler dependencies are stored in a fixed native table sized as `capacity * maxDependenciesPerJob`.
  Rationale: Dependency publication stays bounded and allocation-free; exceeding the configured dependency budget returns `false` before renting a descriptor.
  Date/Author: 2026-06-04 / agent

- Decision: Exception reporting is a cold-path managed report, not a hot-path descriptor allocation.
  Rationale: Throwing already leaves the no-GC path. The scheduler records completed/failed generation state and the latest exception while preserving 0 B normal schedule/complete behavior.
  Date/Author: 2026-06-04 / agent

- Decision: Profiler callbacks use `JobProfilerCallback(in JobProfilerEvent)` and are stored on optional `JobProfilerHooks`.
  Rationale: Hooks are configured outside the hot path, callback payload is a stack struct, and disabled hooks avoid timestamp work entirely.
  Date/Author: 2026-06-04 / agent

- Decision: Work-stealing deque storage is fixed native `NativeArray<T>` and exposes non-throwing `TryPushBottom`, `TryPopBottom`, and `TryStealTop`.
  Rationale: Queue overflow and empty cases must be predictable and allocation-free; queue growth is a scheduler policy decision outside the deque primitive.
  Date/Author: 2026-06-04 / agent

- Decision: Pending dependency execution requeues the handle on the queue it was removed from.
  Rationale: Workers must not execute incomplete jobs. Requeueing to the source queue preserves the stolen/local capacity slot and avoids using the thief queue as an implicit fallback.
  Date/Author: 2026-06-04 / agent

- Decision: Background value-job workers use steal-only queue access for externally published work.
  Rationale: The orchestration thread owns `TryPushBottom`; letting worker threads also pop bottom would violate the deque's single-owner bottom contract. Worker threads claim work through top CAS instead.
  Date/Author: 2026-06-04 / agent

- Decision: Descriptor pool rent/return is protected by a no-allocation lock for the initial threaded runtime.
  Rationale: Worker completion can return slots while the orchestration thread rents new slots. This keeps slot ownership correct now; a lock-free free-list can replace it if profiling shows contention.
  Date/Author: 2026-06-04 / agent

- Decision: Delegate-based jobs are marked with `AllocatingCompatibilityAttribute` instead of being hidden or renamed in this milestone.
  Rationale: Existing callers keep source compatibility, while reflection tests and API metadata make it explicit that these paths allocate managed state and are not safe for no-GC frame hot paths.
  Date/Author: 2026-06-04 / agent

- Decision: Standalone overflow uses bounded failure plus diagnostics for this accepted slice, not hidden native fallback storage.
  Rationale: Silent fallback growth would make capacity mistakes harder to catch and complicate ECS reserved-capacity guarantees. `TrySchedule`/`TryPublish` now expose descriptor, payload, dependency, queue, and requeue overflow counters through `JobRuntimeDiagnostics`.
  Date/Author: 2026-06-04 / agent

- Decision: Only the orchestration thread may call `Schedule` and `Complete`; workers only execute.
  Rationale: Single-producer descriptor ownership and dependency publication are easier to prove and benchmark.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Short value jobs cannot be cancelled after `Schedule`.
  Rationale: Per-job cancellation flags and CTS objects add complexity and overhead. Shutdown and reload quiesce publication and complete the current batch.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Typed results use caller-owned `NativeResult<T>` or `NativeArray<T>`.
  Rationale: Completion slots remain reusable and do not own result lifetime.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Standalone queue overflow returns `false` and increments diagnostics.
  Rationale: Standalone jobs remain predictable under unexpected load without blocking, managed allocation, or hidden capacity growth, while frame scheduling stays fully reserved.
  Date/Author: 2026-06-03 / developer and agent

## Outcomes & Retrospective

Standalone value-job runtime accepted on 2026-06-04 for the current 0.5 slice.

Validation:

- `dotnet build Karpik.Jobs\Karpik.Jobs.csproj -m:1 -nr:false` -> 0 warnings, 0 errors.
- `dotnet test Karpik.Jobs.Tests\Karpik.Jobs.Tests.csproj -m:1 -nr:false` -> 54/54 passed.
- `dotnet test Karpik.Jobs.Tests\Karpik.Jobs.Tests.csproj -c Release -m:1 -nr:false` -> 54/54 passed.
- `dotnet build Karpik.Jobs.Benchmarks\Karpik.Jobs.Benchmarks.csproj -c Release -m:1 -nr:false` -> 0 warnings, 0 errors.
- `dotnet run --project Karpik.Jobs.Benchmarks\Karpik.Jobs.Benchmarks.csproj -c Release --no-build` -> value scheduler measured `0 B` managed allocation for all value benchmark rows.

Benchmark summary from the final run:

- `value-schedule-complete`: 10,000 jobs, `6.230 ms`, `1,605,111 jobs/s`, `0 B`.
- `value-publish-run-next`: 10,000 jobs, `8.733 ms`, `1,145,108 jobs/s`, `0 B`.
- `value-worker-runtime`: 1,000 jobs, `16.668 ms`, `59,996 jobs/s`, `0 B`.
- `value-parallel-batch`: 32,768 items, `11.616 ms`, `2,821,034 items/s`, `0 B`.

Delegate compatibility baseline still allocates by design and remains marked with `AllocatingCompatibilityAttribute`.

## Context And Orientation

Current files:

- `Karpik.Jobs/JobSystem.cs`
- `Karpik.Jobs/JobHandle.cs`
- `Karpik.Jobs/JobCompletion.cs`
- `Karpik.Jobs/JobWrapper.cs`
- `Karpik.Jobs/ObjectPool.cs`
- `Karpik.Jobs/ThreadLocalRandom.cs`

Create focused files rather than growing `JobSystem.cs`:

- `Karpik.Jobs/IJob.cs`
- `Karpik.Jobs/IJobFor.cs`
- `Karpik.Jobs/JobScheduler.cs`
- `Karpik.Jobs/JobBatch.cs`
- `Karpik.Jobs/JobDescriptor.cs`
- `Karpik.Jobs/JobDescriptorPool.cs`
- `Karpik.Jobs/WorkStealingDeque.cs`
- `Karpik.Jobs/JobRuntimeDiagnostics.cs`
- `Karpik.Jobs/JobProfilerHooks.cs`
- `Karpik.Jobs.Tests/Karpik.Jobs.Tests.csproj`
- `Karpik.Jobs.Benchmarks/Karpik.Jobs.Benchmarks.csproj`

Keep `JobSystem` as a compatibility facade or migrate it deliberately. Do not silently make old delegate APIs appear no-GC.

## Real-Time Assessment

This is a scheduler hot path. Value-job `Schedule`, dependency publication, ready-queue operations, completion, work stealing, and batch reuse must allocate `0 B` managed memory after warm-up.

Native fallback allocation is permitted only for standalone overflow, increments diagnostics, and must be absent from the ECS reserved-capacity path.

Avoid closures, delegate invocation, boxing, `ConcurrentQueue<T>`, `ConcurrentBag<T>`, `ManualResetEventSlim` per job, dynamic collection growth, and console logging from workers.

## Plan Of Work

Implement the runtime in five reviewable milestones. Close each milestone with focused tests and Release measurements before moving to the next.

## Milestones

### Milestone 1: Baseline and tests

Add focused tests before replacing internals:

- independent jobs execute exactly once;
- dependency chain preserves order;
- fan-out and fan-in complete;
- `IJobFor`-equivalent parallel ranges cover each index exactly once;
- exception is reported to orchestration thread;
- shutdown completes or rejects publication predictably;
- compatibility delegate API behavior remains documented.

Record Release measurements for current code at `1`, `2`, `4`, and `8` workers where hardware permits:

- independent job throughput;
- chain throughput;
- batch throughput;
- p50/p95/p99 completion latency;
- managed bytes allocated after warm-up.

### Milestone 2: Value-job contracts and descriptors

Add `IJob`, `IJobFor`, `JobHandle`, and scheduling entry points backed by preallocated native descriptor slots from `Karpik.Memory`.

The hot-path scheduling shape is generic and unmanaged:

```csharp
JobHandle Schedule<TJob>(in TJob job)
    where TJob : unmanaged, IJob;

JobHandle ScheduleParallel<TJob>(in TJob job, int length, int batchSize)
    where TJob : unmanaged, IJobFor;
```

Jobs receive unmanaged borrowed `NativeSlice<T>` and `NativeResultHandle<T>` values. They do not capture owning containers or managed references.

Descriptor fields are compact and stable:

- job execution thunk or generated typed executor reference chosen outside per-frame allocation;
- payload storage reference;
- dependency count;
- completion state;
- exception slot or error index;
- generation/version;
- diagnostics timestamps only when profiler hooks are enabled.

Do not store managed closures in no-GC descriptors. If generic payload erasure requires function pointers, isolate unsafe code and test it directly.

### Milestone 3: Dependencies, batches, and caller-owned results

Implement:

- dependency arrays reserved before scheduling;
- batch publication;
- `IJobFor` range partitioning;
- caller-owned `NativeResult<T>` and `NativeArray<T>` examples;
- manual two-stage reduction example: parallel partials followed by dependent reduce job;
- completion and exception propagation;
- profiler hook callbacks or ring-buffer records without worker console output.

Short value jobs remain non-cancellable. Compatibility delegate APIs retain their allocating cancellation behavior if existing callers need it.

### Milestone 4: Work stealing

Replace `ConcurrentQueue<JobWrapper>` with bounded owner-local work-stealing deques:

- owner pushes and pops locally;
- thieves steal from the opposite end;
- memory ordering is documented next to atomic operations;
- queue storage is fixed during active scheduling;
- wrap-around and capacity exhaustion are tested;
- descriptor pool native fallback is counted and observable.

Stress:

- many jobs with `1`, `2`, `4`, and `8` workers;
- repeated wrap-around;
- stealing under skewed distribution;
- shutdown during queued and running work;
- exceptions under load;
- exactly-once execution across repeated runs.

### Milestone 5: Compatibility facade and acceptance

Keep old `Action` and `Func<T>` APIs under a clearly named/documented allocating compatibility path. Migrate internal engine callers that belong on the no-GC path.

Add docs and benchmark output summary. Build the project and run focused tests.

## Concrete Steps

Commands run from `C:\Users\artem\RiderProjects\KarpikEngine`.

```powershell
dotnet build Karpik.Jobs\Karpik.Jobs.csproj -m:1 -nr:false
dotnet test Karpik.Jobs.Tests\Karpik.Jobs.Tests.csproj -m:1 -nr:false
dotnet run --project Karpik.Jobs.Benchmarks\Karpik.Jobs.Benchmarks.csproj -c Release
```

## Validation And Acceptance

Accept when:

- exactly-once, dependencies, fan-in/out, `IJobFor`, exceptions, shutdown, wrap-around, and contention tests pass;
- preallocated value scheduling measures `0 B` managed allocation after warm-up;
- overflow paths increment diagnostics and the reserved-capacity normal path keeps counters at zero;
- throughput and tail-latency evidence is recorded;
- delegate APIs remain compatibility-only and are not used by ECS scheduling.

## Idempotence And Recovery

Keep old delegate behavior until compatibility tests pass. Hide queue replacement behind an internal abstraction so the previous correct queue can be restored if stress tests expose lost work or shutdown races.

## Artifacts And Notes

ADR `docs/02_ADR/scheduler-jobs-runtime.md` records queue memory-ordering rules, overflow policy, compatibility boundary, and validation commands.
