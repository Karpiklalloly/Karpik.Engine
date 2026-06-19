---
name: karpik-memory
description: Use when Codex works with Karpik.Memory, native containers, unmanaged allocation, NativeArray, NativeSlice, NativeResult, NativeLinearAllocator, NativePool, NativeArena, SimpleNativeArray migration, no-GC storage, allocation-budget tests, or memory ownership in KarpikEngine hot paths.
---

# Karpik Memory

## Core Rule

Treat `Karpik.Memory` as a real-time storage foundation, not a general convenience library. Hot paths must use preallocated native storage, dense traversal, explicit lifetime, and `0 B` managed allocation after warm-up.

## Pick The Right Primitive

| Need | Use | Hot-path rule |
| --- | --- | --- |
| Long-lived dense unmanaged array | `NativeArray<T>` | Allocate outside hot path; index/clear in hot path |
| Borrowed array view for jobs/scheduler | `NativeSlice<T>` | Never outlive owner; do not store beyond owner reset/dispose |
| Caller-owned one-value job result | `NativeResult<T>` | Owner stays with caller |
| Borrowed result slot for value jobs | `NativeResultHandle<T>` | Job writes handle; caller owns disposal |
| Per-frame/preallocated scratch bytes | `NativeLinearAllocator` | Allocate/reset only within fixed capacity; no growth |
| Reusable fixed descriptor/result slots | `NativePool<T>` | Rent/return only; returned handles are invalid |
| Startup/graph-construction arena | `NativeArena` | Outside-frame only; growth/reset may allocate managed metadata |
| Old jobs compatibility | `SimpleNativeArray<T>` | Do not expand usage; migrate toward `NativeArray<T>` |

## Ownership Rules

- Owners are `IDisposable` objects: `NativeArray<T>`, `NativeResult<T>`, `NativeLinearAllocator`, `NativePool<T>`, `NativeArena`.
- Borrowed values are structs: `NativeSlice<T>`, `NativeResultHandle<T>`, `NativeMemorySlice`, `NativePoolHandle<T>`.
- Do not copy ownership into job descriptors. Jobs receive borrowed handles/slices only.
- Do not keep borrowed values after owner `Dispose`, allocator `Reset`, or pool `Return`.
- Use `where T : unmanaged`; never store managed references in native containers.
- Keep `NativeArena` out of frame loops, ECS `Run`, scheduler dispatch, render preparation, and network hot paths.

## Debug vs Release

Debug builds validate stale borrowed views and handles through ownership tokens. Release builds avoid borrowed-view registry checks for speed. Do not rely on Release to catch lifetime bugs; prove them with Debug tests.

## Testing And Verification

For any memory primitive change, add tests before implementation:

- invalid capacity/length/alignment;
- bounds checks;
- access after owner disposal;
- stale borrowed view/handle after reset, return, or dispose in Debug;
- Release allocation-budget test for steady-state hot operations.

Use targeted commands from the workspace root:

```powershell
dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -c Release -m:1 -nr:false
dotnet build Karpik.Memory\Karpik.Memory.csproj -m:1 -nr:false
```

If `Karpik.Jobs` compatibility is touched:

```powershell
dotnet test Karpik.Jobs.Tests\Karpik.Jobs.Tests.csproj -m:1 -nr:false
dotnet build Karpik.Jobs\Karpik.Jobs.csproj -m:1 -nr:false
```

## Common Mistakes

- Using `NativeArena` as a no-GC frame allocator. It is outside-frame storage.
- Returning `NativePoolHandle<T>` late or using it after `Return`.
- Storing `NativeSlice<T>` in long-lived services or ECS components.
- Adding LINQ, delegates, closures, `List<T>`, `Dictionary<TKey,TValue>`, or boxing to memory hot paths.
- Treating `SimpleNativeArray<T>` as the future API. It is a compatibility wrapper only.

## References

- ADR: `docs/02_ADR/native-memory-ownership.md`
- ExecPlan: `plans/native-memory-foundation-execplan.md`
- Code: `Karpik.Memory/`, `Karpik.Memory.Tests/`, `Karpik.Memory.Benchmarks/`
