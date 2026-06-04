# ADR: Standalone Value Jobs Runtime

## Status

Accepted for the standalone `Karpik.Jobs` 0.5 runtime slice.

## Context

The legacy `JobSystem` publishes managed delegates through `ConcurrentQueue<JobWrapper>` and allocates managed completion, cancellation, continuation, and wrapper state. It remains useful as a compatibility API, but it is not suitable for frame hot paths or ECS scheduling.

The 0.5 standalone runtime needs value-type jobs, predictable descriptor ownership, bounded queues, explicit diagnostics, and `0 B` managed allocation after warm-up on the normal scheduling path.

## Decision

Add a separate `JobScheduler` value-job runtime:

- jobs are unmanaged payloads implementing `IJob` or `IJobFor`;
- scheduling stores payloads in preallocated native descriptor and payload storage;
- dependencies are stored in a fixed native table;
- ready work is published to fixed-capacity `WorkStealingDeque<ValueJobHandle>` queues;
- background workers claim externally published work through `TryStealTop`;
- the orchestration thread owns descriptor publication and queue bottom pushes;
- `JobSystem` delegate APIs stay source-compatible but are marked with `AllocatingCompatibilityAttribute`.

## Memory Ordering

`WorkStealingDeque<T>` has a single bottom owner. The owner writes the item slot before publishing the new bottom with `Volatile.Write`. Thieves read top/bottom with `Volatile.Read` and claim a top slot with `Interlocked.CompareExchange`.

When `JobScheduler.TryPublish` is called by the orchestration thread, worker threads must not call `TryPopBottom` on that queue. Worker loops therefore use a steal-only drain, including for their assigned queue. Manual `TryRunNext` is available only while background workers are not running.

Completion generation visibility uses `Volatile.Write` and `Volatile.Read`. Descriptor reuse can become observable after completion; callers that immediately reuse capacity-1 schedulers must wait for descriptor return as well as completion.

## Overflow Policy

The accepted standalone policy is bounded failure with diagnostics, not hidden fallback growth.

`TrySchedule` returns `false` and increments diagnostics on descriptor exhaustion, payload overflow, dependency budget exhaustion, or invalid dependency handles. `TryPublish` returns `false` and increments diagnostics on worker queue overflow. Pending dependency requeue overflow increments diagnostics.

No managed fallback allocation is allowed in the normal path. Native fallback storage is not part of this accepted slice; if added later for non-ECS standalone overload handling, it must be explicit, prevalidated, benchmarked, and disabled for ECS reserved-capacity scheduling.

## Consequences

The normal value-job paths are measurable at `0 B` managed allocation after warm-up. Overflow is observable and predictable, but callers must size descriptor and queue capacity deliberately. The initial threaded runtime uses a no-allocation lock around descriptor rent/return; this is correct for slot ownership and can be replaced with a lock-free free-list only after profiling.

Legacy delegate APIs continue to allocate and are intentionally excluded from the no-GC scheduler.

## Validation

Accepted validation commands from `C:\Users\artem\RiderProjects\KarpikEngine`:

```powershell
dotnet build Karpik.Jobs\Karpik.Jobs.csproj -m:1 -nr:false
dotnet test Karpik.Jobs.Tests\Karpik.Jobs.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Jobs.Tests\Karpik.Jobs.Tests.csproj -c Release -m:1 -nr:false
dotnet build Karpik.Jobs.Benchmarks\Karpik.Jobs.Benchmarks.csproj -c Release -m:1 -nr:false
dotnet run --project Karpik.Jobs.Benchmarks\Karpik.Jobs.Benchmarks.csproj -c Release --no-build
```
