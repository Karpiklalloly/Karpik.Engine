---
title: "Native memory ownership"
date: "2026-06-03"
status: "accepted"
tags:
  - adr
  - architecture
  - memory
  - performance
---

# Native Memory Ownership

> Status: accepted
> Date: 2026-06-03
> Owners: developer and Codex
> Related ExecPlan: [[../../plans/native-memory-foundation-execplan]]

## Context

`Karpik.Jobs/SimpleNativeArray<T>` already used unmanaged memory, but it had unchecked indexing, no invalid length guard, no active allocation diagnostics, and copied owner values could create stale aliases or double-dispose hazards.

The `0.5` scheduler/jobs foundation needs reusable native storage for job descriptors, scratch buffers, ECS graph traversal, and caller-owned results. Hot operations must use dense memory, explicit lifetime, and `0 B` managed allocation after warm-up. Debug builds should catch ownership misuse, while Release hot paths must not pay for registry lookups on every index or handle access.

## Decision

Add `Karpik.Memory` as a standalone project. Jobs may depend on memory; memory does not depend on jobs.

Owning containers are heap objects with explicit `Dispose`:

- `NativeArray<T>`
- `NativeResult<T>`
- `NativeLinearAllocator`
- `NativePool<T>`
- `NativeArena`

Borrowed hot-path values are unmanaged structs:

- `NativeSlice<T>`
- `NativeResultHandle<T>`
- `NativeMemorySlice`
- `NativePoolHandle<T>`

All generic storage requires `where T : unmanaged`. Owners use `NativeAllocation` internally for aligned unmanaged allocation, active allocation diagnostics, and version tokens. Debug builds validate stale borrowed views and handles through tokens. Release builds keep owner-level argument/dispose checks but avoid per-access diagnostics on borrowed views.

`NativeArena` is an outside-frame owner-side allocator. Its block growth and reset may allocate managed block metadata and must not be used as a steady-state frame allocator. Frame paths should use preallocated `NativeLinearAllocator`, `NativeArray<T>`, `NativeResult<T>`, or `NativePool<T>`.

`Karpik.Jobs/SimpleNativeArray<T>` remains as a compatibility wrapper over `NativeArray<T>` until jobs runtime migration removes or replaces the old API deliberately.

## Alternatives Considered

- Keep `SimpleNativeArray<T>` and add checks there. Rejected because jobs, scheduler, and render-adjacent paths need more than one array type and a shared ownership model.
- Make owning containers structs. Rejected because copied owners can both believe they own the same pointer, making double-dispose and stale aliases harder to prevent.
- Keep full borrowed-view validation in Release. Rejected because per-access registry lookups are not suitable for hot paths.
- Use BenchmarkDotNet immediately. Deferred because the current milestone needs local, network-independent allocation evidence first. A lightweight benchmark project records timing and allocation smoke numbers without new package restore.

## Consequences

- Owner lifetime is explicit and testable.
- Borrowed values are small structs suitable for job payloads and scheduler metadata.
- Debug builds catch stale borrowed views after dispose, reset, return, or owner mismatch.
- Release builds are faster but trust borrowed view lifetime; misuse there is undefined and must be caught by Debug tests and higher-level scheduler validation.
- `NativeArena` is useful for setup and graph construction but not a frame allocator.
- `Karpik.Jobs` now references `Karpik.Memory`.

## Validation

- `dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -m:1 -nr:false`
- `dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -c Release -m:1 -nr:false`
- `dotnet test Karpik.Jobs.Tests\Karpik.Jobs.Tests.csproj -m:1 -nr:false`
- `dotnet build Karpik.Memory\Karpik.Memory.csproj -m:1 -nr:false`
- `dotnet build Karpik.Jobs\Karpik.Jobs.csproj -m:1 -nr:false`
- `dotnet run --project Karpik.Memory.Benchmarks\Karpik.Memory.Benchmarks.csproj -c Release`

Release benchmark smoke on 2026-06-03:

- `NativeArray<int>` traversal: `155.570 ms`, `0 B` managed.
- `NativeLinearAllocator` allocate/reset: `22.005 ms`, `0 B` managed.
- `NativePool<int>` rent/return: `44.653 ms`, `0 B` managed.
- `NativeResult<int>` handle write: `76.766 ms`, `0 B` managed.
- outside-frame `NativeArena` allocate/reset: `22.127 ms`, `1680000 B` managed over 10000 iterations.

## Links

- ExecPlan: [[../../plans/native-memory-foundation-execplan]]
- Related code: `Karpik.Memory/`, `Karpik.Memory.Tests/`, `Karpik.Memory.Benchmarks/`, `Karpik.Jobs/SimpleNativeArray.cs`
