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
- [ ] Replace `ConcurrentQueue<JobWrapper>` with bounded work-stealing deques.
- [ ] Keep delegate compatibility APIs explicitly allocating.
- [ ] Run standalone acceptance gate.

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

- Decision: Only the orchestration thread may call `Schedule` and `Complete`; workers only execute.
  Rationale: Single-producer descriptor ownership and dependency publication are easier to prove and benchmark.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Short value jobs cannot be cancelled after `Schedule`.
  Rationale: Per-job cancellation flags and CTS objects add complexity and overhead. Shutdown and reload quiesce publication and complete the current batch.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Typed results use caller-owned `NativeResult<T>` or `NativeArray<T>`.
  Rationale: Completion slots remain reusable and do not own result lifetime.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Standalone queue overflow may allocate native fallback storage and increment counters; ECS integration may not use that fallback.
  Rationale: Standalone jobs remain usable under unexpected load without blocking or managed allocation, while frame scheduling stays fully reserved.
  Date/Author: 2026-06-03 / developer and agent

## Outcomes & Retrospective

No outcome yet.

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
- native overflow fallback increments diagnostics and is absent when ECS-reserved capacity is used;
- throughput and tail-latency evidence is recorded;
- delegate APIs remain compatibility-only and are not used by ECS scheduling.

## Idempotence And Recovery

Keep old delegate behavior until compatibility tests pass. Hide queue replacement behind an internal abstraction so the previous correct queue can be restored if stress tests expose lost work or shutdown races.

## Artifacts And Notes

Create ADR `docs/02_ADR/scheduler-jobs-runtime.md` before changing public contracts. Record queue memory-ordering rules and overflow policy there.
