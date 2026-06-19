# Backlog

Рабочий список конкретных задач, багов и технических долгов, которые не должны раздувать milestone roadmap. Для крупных изменений заводить ExecPlan в `plans/`.

## P0 - Runtime Correctness

### Migrate gameplay-loop scheduling to no-GC value jobs

**Problem:** gameplay hot paths must not use delegate-based `JobSystem.Enqueue(...)`. Delegate jobs allocate managed completion/cancellation/continuation/wrapper state and are now explicitly marked as `AllocatingCompatibility`. Regular `Update`, `FixedUpdate`, ECS `Run`, `IEcsRunParallel`, simulation tick, prediction, replication, and server tick scheduling must use the new value-job runtime or a no-GC engine adapter over it.

**Scope:** do not mechanically replace every `Enqueue` call. Migrate only gameplay/frame hot paths first. Keep legacy `JobSystem` for tooling, loading, rare background work, async convenience, and compatibility APIs.

**Affected places:**

- `Modules/Shared/ECS/ECS.Core/IEcsRunParallel.cs`
- `Modules/Shared/ECS/ECS.Core/SystemExecutionNode.cs`
- `Karpik.Jobs/JobScheduler.cs`
- `Karpik.Jobs/JobSystem.cs`
- `plans/ecs-update-scheduler-execplan.md`

**Acceptance criteria:**

- [ ] ECS/gameplay parallel scheduling no longer publishes frame work through `JobSystem.Enqueue(...)`.
- [ ] A scheduler adapter hides `JobScheduler` details from gameplay systems.
- [ ] Job payloads used in the gameplay loop are unmanaged structs with caller-owned native handles/slices/results only.
- [ ] Delegate `JobSystem` remains available only as an off-hot-path compatibility path.
- [ ] Allocation tests prove steady-state gameplay/ECS scheduling is `0 B` managed allocation after warm-up.
- [ ] Tests cover dependencies, conflict/sequential barriers, shutdown/reload quiescence, and worker publication failure diagnostics.

### Освобождать физические тела при удалении ECS entity

**Проблема:** при удалении сущности ECS связанное тело может остаться в `IPhysicsWorld2D`. На disconnect это оставляет в физическом мире stale body, которое продолжает участвовать в simulation/collision queries без живой gameplay entity.

**Затронутые места:**

- `Modules/Shared/Physics/Physics2D.Core/ECS/Physics2DBodyDestroyer.cs`
- `Modules/Shared/Physics/Physics2D.Core/ECS/Components.cs`
- `MyGame/Server/MyGame.Server.Main/Systems/NetworkSystem.cs`

**Acceptance criteria:**

- [ ] Удаление entity с `PhysicsBodyRef` гарантированно вызывает `IPhysicsWorld2D.DestroyBody`.
- [ ] Disconnect cleanup игрока не оставляет тела в физическом мире.
- [ ] Cleanup не аллоцирует в ECS/system hot path после warm-up.
- [ ] Добавлен минимальный тест или smoke-сценарий на удаление entity с physics body.

## P2 - Отложенные расширения после 0.5

Эти пункты сознательно исключены из release gate `0.5`. Возвращаться к ним нужно только при наличии измеренного bottleneck или конкретного subsystem consumer.

### Parallel scheduling для дополнительных lifecycle-фаз

**Отложено:** в `0.5` на workers выполняются только `ISystemUpdate` и read-only `ISystemRenderPrepare`. `ISystemFixedUpdate`, `ISystemBegin`, `ISystemLateUpdate` и `ISystemRender` остаются последовательными.

**Условие возврата:** профилирование показывает bottleneck в fixed simulation или late update, а затронутые системы имеют явную deterministic semantics.

### Nested и multi-producer no-GC scheduling в Karpik.Jobs

**Отложено:** no-GC `Karpik.Jobs` принимает `Schedule` и `Complete` только от orchestration thread. Workers исполняют jobs, но не публикуют дочерние jobs.

**Условие возврата:** измеренный workload нельзя эффективно выразить orchestration-thread batches и dependencies.

### Расширенный standalone jobs API

**Отложено:** priorities, delayed jobs, long-running jobs и индивидуальная отмена коротких value jobs не входят в `0.5`. После публикации короткая job должна завершиться; shutdown и hot reload останавливают публикацию новых batches и дожидаются активного batch.

**Условие возврата:** конкретному subsystem нужен один из этих primitives и его нельзя вынести в отдельный non-real-time task path.

### Специализированный parallel reduction API

**Отложено:** `0.5` даёт caller-owned `NativeArray<T>` / `NativeResult<T>` и композицию dependencies, но не отдельный tree-reduction primitive.

**Условие возврата:** в engine code повторяется ручная многоуровневая reduction или benchmark показывает bottleneck последовательной финальной свёртки.

### Полный runtime safety tracking native containers

**Отложено:** `Karpik.Memory` использует lightweight ownership/version tokens, disposed checks, Debug bounds checks и `[ReadOnly]` metadata. Полный Unity-style runtime tracking read/write dependencies для каждого container instance не входит в `0.5`.

**Условие возврата:** ошибки aliasing остаются частыми после generated scheduler validation либо public jobs API нужен безопасный third-party scheduling вне engine graph.

### Более широкий ECS analyzer subset

**Отложено:** ECS codegen fail-closed для reflection, `dynamic`, unresolved dispatch, opaque delegates, внешних ECS-capable helpers без summaries, сложного generic aliasing и `Unsafe` aliases. Полное whole-program доказательство для произвольного C# не входит в `0.5`.

**Условие возврата:** корректные системы требуют слишком много `[Reads<T>]` / `[Writes<T>]` summaries или `[SequentialSystem]` opt-outs. Перед ослаблением safety оценить более сильный points-to, alias и interprocedural analysis.

### Preallocated typed main-thread command queue

**Отложено:** обязательная per-frame работа не должна использовать `MainThreadScheduler` из `ISystemUpdate`. Текущий delegate-based scheduler остаётся редким off-hot-path compatibility channel.

**Условие возврата:** реальному subsystem нужны частые simulation-to-main-thread команды, которые нельзя разместить в `ISystemBegin`, `ISystemRenderPrepare` или `ISystemRender`.

### Deterministic parallel scheduler mode

**Отложено:** deterministic mode исполняет generated graph последовательно в стабильном topological order. `0.5` не обещает deterministic worker interleaving.

**Условие возврата:** replay или networking требуют воспроизводимого parallel execution, а benchmark оправдывает дополнительные scheduling constraints.

### Настраиваемая глубина render-command buffering

**Отложено:** Graphics.Core использует фиксированный triple buffering для command-set ownership. Произвольная настройка количества sets не входит в `0.5`.

**Условие возврата:** platform-specific GPU latency measurements показывают, что три sets недостаточны или избыточны.

