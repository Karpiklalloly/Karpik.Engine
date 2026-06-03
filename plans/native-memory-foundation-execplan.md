# Build the unmanaged memory foundation

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Create `Karpik.Memory`, a standalone unmanaged memory project used by `Karpik.Jobs` and future runtime systems. The public API provides explicit lifetime, dense storage, ref-friendly access, and Debug misuse diagnostics without adding steady-state managed allocations.

## Progress

- [x] (2026-06-03 00:33 +04:00) Design agreed as child plan 1 of `plans/scheduler-jobs-memory-execplan.md`.
- [ ] Record baseline behavior of `Karpik.Jobs/SimpleNativeArray.cs`.
- [ ] Create `Karpik.Memory` and focused tests.
- [ ] Implement ownership tracking and Debug diagnostics.
- [ ] Implement required allocators and containers.
- [ ] Add benchmarks and allocation checks.
- [ ] Migrate the jobs prototype off `SimpleNativeArray<T>` where applicable and close the gate.

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

No outcome yet.

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
