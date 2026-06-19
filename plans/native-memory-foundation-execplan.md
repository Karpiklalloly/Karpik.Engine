# Build the unmanaged memory foundation

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Create `Karpik.Memory`, a standalone unmanaged memory project used by `Karpik.Jobs` and future runtime systems. The public API provides explicit lifetime, dense storage, ref-friendly access, and Debug misuse diagnostics without adding steady-state managed allocations.

## Progress

- [x] (2026-06-03 00:33 +04:00) Design agreed as child plan 1 of `plans/scheduler-jobs-memory-execplan.md`.
- [x] (2026-06-03 21:29 +04:00) Recorded baseline behavior of `Karpik.Jobs/SimpleNativeArray.cs`: unmanaged allocation/free only, unchecked indexing, no invalid length/overflow guard, no active allocation diagnostics, and copied owner values can create stale aliases or double-dispose hazards.
- [x] (2026-06-03 20:59 +04:00) Created `Karpik.Memory` and `Karpik.Memory.Tests`; watched ownership diagnostics tests fail on missing `NativeAllocation`, `NativeAllocationToken`, `NativeMemoryDiagnostics`, and `NativeAllocationKind`, then pass after implementation.
- [x] (2026-06-03 20:59 +04:00) Implemented the first allocation ownership spine: aligned unmanaged allocation, explicit dispose, active-allocation diagnostics, double-dispose failure, disposed-token failure, and cross-allocation token validation.
- [x] (2026-06-03 21:06 +04:00) Implemented Milestone 2 containers and tests for `NativeArray<T>`, `NativeSlice<T>`, `NativeResult<T>`, and `NativeResultHandle<T>`: negative/zero length, bounds, span/clear, access after dispose, double dispose, and stale borrowed view/handle checks.
- [x] (2026-06-03 21:29 +04:00) Implemented Milestone 3 allocators and tests for `NativeLinearAllocator`, `NativePool<T>`, and `NativeArena`: alignment, capacity exhaustion, reset/reuse, multi-block arena growth, stale borrowed views/handles, double return, and disposal invalidation.
- [x] (2026-06-03 22:14 +04:00) Added Release allocation-budget tests and `Karpik.Memory.Benchmarks`. `dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -c Release -m:1 -nr:false` passed 51/51. `dotnet run --project Karpik.Memory.Benchmarks\Karpik.Memory.Benchmarks.csproj -c Release` measured: `NativeArray<int>` traversal `0 B`, `NativeLinearAllocator` allocate/reset `0 B`, `NativePool<int>` rent/return `0 B`, `NativeResult<int>` handle write `0 B`, and outside-frame `NativeArena` allocate/reset `1680000 B` managed over 10000 iterations due to managed block metadata. Latest smoke timings: array `155.570 ms`, linear allocator `22.005 ms`, pool `44.653 ms`, result `76.766 ms`, arena `22.127 ms`.
- [x] (2026-06-03 21:32 +04:00) Migrated `Karpik.Jobs/SimpleNativeArray<T>` to wrap `Karpik.Memory.NativeArray<T>` while preserving `Length`, `ref` indexer, and `Dispose`; added `Karpik.Jobs.Tests` compatibility coverage and verified double-dispose now fails through the shared ownership contract.
- [x] (2026-06-03 22:14 +04:00) Added accepted ADR `docs/02_ADR/native-memory-ownership.md`.

## Surprises & Discoveries

- Observation: `SimpleNativeArray<T>` already uses `NativeMemory.Alloc` and `NativeMemory.Free`, but indexing is unchecked and copying the struct can produce stale aliases and double-dispose risks.
  Evidence: `Karpik.Jobs/SimpleNativeArray.cs`.

## Decision Log

- Decision: Put allocators and containers in standalone `Karpik.Memory`.
  Rationale: Unsafe ownership is an independent responsibility. Jobs depend on memory; memory must not depend on jobs.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Require `where T : unmanaged` for generic containers and pools.
  Rationale: Managed references inside native storage would violate GC visibility and ownership rules.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Keep Debug safety richer than Release safety.
  Rationale: Bounds, stale-handle, leak, and poison checks are valuable during development but must not inflate the steady-state release path.
  Date/Author: 2026-06-03 / developer and agent

## Outcomes & Retrospective

`Karpik.Memory` now provides the first native ownership foundation for `0.5`: aligned allocation ownership, Debug misuse diagnostics, dense containers, caller-owned result storage, a preallocated linear allocator, a fixed-capacity pool, and an outside-frame arena. Release allocation checks show `0 B` managed allocation for preallocated array traversal, linear allocate/reset, pool rent/return, and result handle writes. `NativeArena` remains explicitly outside-frame because repeated block growth/reset allocates managed metadata.

## Context And Orientation

Create:

- `Karpik.Memory/Karpik.Memory.csproj`
- `Karpik.Memory/NativeAllocation.cs`
- `Karpik.Memory/NativeMemoryDiagnostics.cs`
- `Karpik.Memory/NativeArena.cs`
- `Karpik.Memory/NativeLinearAllocator.cs`
- `Karpik.Memory/NativePool.cs`
- `Karpik.Memory/NativeArray.cs`
- `Karpik.Memory/NativeResult.cs`
- `Karpik.Memory/NativeResultHandle.cs`
- `Karpik.Memory/NativeSlice.cs`
- `Karpik.Memory.Tests/Karpik.Memory.Tests.csproj`
- `Karpik.Memory.Benchmarks/Karpik.Memory.Benchmarks.csproj`

Update:

- `KarpikEngine.slnx`
- `Generated/KarpikModuleCatalog.props` through Configurator generation after adding projects
- `Karpik.Jobs/Karpik.Jobs.csproj`
- `Karpik.Jobs/SimpleNativeArray.cs`

`NativeAllocation` is the small internal owner record: pointer, byte length, alignment, disposed state, and Debug version token.

Owning containers are initialization-time owner objects with explicit `Dispose`. Hot-path jobs receive unmanaged borrowed values such as `NativeSlice<T>` and `NativeResultHandle<T>`, which contain pointer, length where applicable, and Debug version token. Do not copy an owning value into a job descriptor and do not let two copied values both believe they own the same pointer.

## Real-Time Assessment

Allocators and containers will be used in scheduler, worker, and render-adjacent paths. Preallocated operations must perform no managed allocation. `NativeMemory.Alloc` and `NativeMemory.Free` are explicit lifetime operations and must not appear in steady-state ECS frame scheduling.

Release hot operations should be pointer arithmetic plus cheap boundary checks at public API edges. Debug may consult the leak registry and poison memory during allocate, reset, return, and dispose.

## Plan Of Work

Implement the foundation in four reviewable milestones. Close each milestone with its focused tests before moving to the next.

## Milestones

### Milestone 1: Project and diagnostics spine

Create `Karpik.Memory`, add it to the solution, and add focused xUnit tests. Implement the internal allocation owner and `NativeMemoryDiagnostics`.

Debug diagnostics:

- monotonic allocation ID and version;
- pointer, size, alignment, allocation kind, and disposed state;
- leak registry;
- optional poison-fill controlled by a debug option;
- explicit double-dispose and stale-handle failures.

Release behavior:

- no registry lookup in steady-state indexing or allocation-free reset paths;
- retain argument validation for invalid sizes, alignment, and disposal misuse at public boundaries.

Validation:

```powershell
dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -m:1 -nr:false
```

### Milestone 2: Containers

Implement:

- `NativeArray<T>` owner with allocation, length, `Span<T>`, `ReadOnlySpan<T>`, `ref T this[int]`, clear, dispose, and unmanaged `NativeSlice<T>` borrowed view;
- `NativeResult<T>` owner with one-element storage, `ref Value`, dispose, and unmanaged `NativeResultHandle<T>` borrowed view suitable for value jobs.

Add misuse tests:

- negative length;
- zero-length behavior;
- index below zero and at length;
- access after dispose;
- double dispose;
- copied stale view after owner version changes;
- `Span<T>` length and clear behavior.

Validation:

```powershell
dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -m:1 -nr:false
```

### Milestone 3: Allocators

Implement:

- `NativeArena`: allocates aligned regions from owned blocks and releases all blocks together;
- `NativeLinearAllocator`: preallocated bump allocator with aligned allocate and explicit `Reset`;
- `NativePool<T>`: fixed-capacity reusable slots with rent/return, generation checks, and overflow diagnostics.

Do not build a general garbage collector, compaction layer, or polymorphic allocator hierarchy. Prefer small concrete APIs.

Add tests:

- alignment for `1`, `2`, `4`, `8`, `16`, `32`, and `64`;
- arena multi-block growth outside active frame;
- linear reset and stale borrowed views;
- pool rent/return, capacity exhaustion, repeated reuse, stale generation, and double return;
- leak registry empty after test teardown;
- poison-fill behavior in Debug.

Validation:

```powershell
dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -m:1 -nr:false
```

### Milestone 4: Benchmarks and migration boundary

Create `Karpik.Memory.Benchmarks/Karpik.Memory.Benchmarks.csproj` and measure:

- sequential `NativeArray<T>` traversal;
- `NativeLinearAllocator.Allocate` and `Reset`;
- `NativePool<T>.Rent` and `Return`;
- arena allocation outside frame execution;
- managed bytes allocated after warm-up.

Replace or deprecate `Karpik.Jobs/SimpleNativeArray.cs` only after `Karpik.Jobs` references `Karpik.Memory`. Preserve source compatibility temporarily if existing call sites require it.

Validation:

```powershell
dotnet build Karpik.Memory\Karpik.Memory.csproj -m:1 -nr:false
dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -m:1 -nr:false
dotnet build Karpik.Jobs\Karpik.Jobs.csproj -m:1 -nr:false
```

## Concrete Steps

Commands run from `C:\Users\artem\RiderProjects\KarpikEngine`.

```powershell
dotnet run --project Configurator\Configurator.csproj -- --generate
dotnet run --project Configurator\Configurator.csproj -- --validate
dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -m:1 -nr:false
dotnet run --project Karpik.Memory.Benchmarks\Karpik.Memory.Benchmarks.csproj -c Release
dotnet build Karpik.Jobs\Karpik.Jobs.csproj -m:1 -nr:false
```

## Validation And Acceptance

Accept when all five required public primitives exist, misuse tests pass, Debug diagnostics find no leak after test teardown, and preallocated Release operations allocate `0 B` managed memory after warm-up.

## Idempotence And Recovery

Project generation and tests are safe to rerun. Keep `SimpleNativeArray<T>` until downstream jobs migration passes. If a public container design allows ambiguous ownership after struct copy, stop and revise the API before integrating jobs.

## Artifacts And Notes

Create ADR `docs/02_ADR/native-memory-ownership.md` before closing this plan.
