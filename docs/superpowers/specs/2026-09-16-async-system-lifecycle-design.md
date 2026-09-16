# Async system lifecycle

## Purpose

Allow a simulation to complete required asynchronous initialization before its
first frame and asynchronous cleanup before its ECS pipeline and dependency
injection scopes are released. Content preloading is the first consumer.

## Scope

Add two opt-in lifecycle contracts in `Karpik.Engine.Core`:

```csharp
public interface ISystemAsyncInit : ISystem
{
    ValueTask InitAsync(CancellationToken cancellationToken);
}

public interface ISystemAsyncDestroy : ISystem
{
    ValueTask DestroyAsync();
}
```

Existing `ISystemInit` and `ISystemDestroy` remain unchanged. They are the
synchronous compatibility lifecycle and must not block on these new contracts.

## Lifecycle and ordering

`EngineRunner.SetupAsync` builds the container, resolves systems and executes
the existing synchronous pipeline initialization. It then awaits every
`ISystemAsyncInit` in registered pipeline order. `Run` is unavailable until
the full initialization completes.

`EngineRunner.DestroyAsync` awaits every `ISystemAsyncDestroy` in reverse
registered pipeline order. It then destroys the pipeline, disposes runners,
and asynchronously disposes Simulation, ModSet, and Engine scopes. The
shutdown contract deliberately has no cancellation token: cleanup must finish
once shutdown has begun.

If an async initializer fails, setup fails and the normal teardown path cleans
up the partially-created runtime. If an async destroyer fails, remaining
destroyers and resource cleanup still run; failures are reported together as
an `AggregateException`.

## Runner integration

The Dragon ECS pipeline remains synchronous. `ISystemAsyncInit` and
`ISystemAsyncDestroy` are collected by Karpik's `Builder`; they are not
wrapped as Dragon `IEcsInit` or `IEcsDestroy` processes. The runner exposes
asynchronous setup and shutdown at the bootstrap/host boundary. A worker host
may synchronously wait at those boundaries to retain main-thread affinity; it
must never wait from a frame phase.

## Real-time constraints

No async lifecycle operation executes from `Begin`, `FixedUpdate`, `Update`,
`LateUpdate`, `RenderPrepare`, or `Render`. Allocation and I/O during startup
and teardown are permitted. Frame execution remains synchronous and
allocation-free after warm-up.

## Validation

Runner tests must prove that async initialization completes before the first
frame, preserves registration order, and that asynchronous destruction runs in
reverse order before scopes are disposed. Existing synchronous lifecycle tests
remain valid.
