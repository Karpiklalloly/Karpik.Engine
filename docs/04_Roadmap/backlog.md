# Backlog

Рабочий список конкретных задач, багов и технических долгов, которые не должны раздувать milestone roadmap. Для крупных изменений заводить ExecPlan в `plans/`.

## P0 - Runtime Correctness

### Migrate gameplay-loop scheduling to no-GC value jobs

**Status (2026-11-01):** implemented on the task branch; merge pending. Public Update/RenderPrepare scheduling already uses value jobs. The remaining legacy `EcsRunParallelRunner` now uses unmanaged jobs and a startup-owned registry handle instead of delegate enqueue.

**Result:** the adapter reserves a whole frame before publishing work, publishes only nodes whose predecessors have completed, and drains all work before returning or releasing its registry. Worker exceptions reach the caller after the batch drains. Rejected publication drains reserved work before reporting failure. Caller-owned dependency readiness avoids worker-side requeue into a single-producer deque.

**Scope:** legacy `JobSystem` remains available for tooling, loading, rare background work and compatibility APIs. The separate Karpik.Jobs product and the public Update scheduler are unchanged.

**Changed places:**

- `Modules/Shared/ECS/ECS.Core/IEcsRunParallel.cs`
- `tests/ECS.Core.Tests/ParallelRunnerRuntimeTests.cs`

**Acceptance criteria:**

- [x] ECS/gameplay parallel scheduling no longer publishes frame work through `JobSystem.Enqueue(...)`.
- [x] A scheduler adapter hides `JobScheduler` details from gameplay systems.
- [x] Jobs carry only an unmanaged registry pointer and system index; the registry is created at initialization and released after workers stop.
- [x] Delegate `JobSystem` remains available as an off-hot-path compatibility path.
- [x] Allocation regression proves `0 B` on the orchestration thread across 64 frames after warm-up; worker bodies and dispatch use no per-job managed state.
- [x] Runtime tests cover independent readers, conflict/unknown-access ordering, worker errors, rejected publication, empty graphs and repeated destroy/reinitialize cycles.

### Освобождать физические тела при удалении ECS entity

**Статус (2026-11-01):** реализовано в task-ветке, ожидает слияния.

**Проблема:** удалённая entity исчезает из aspect query раньше, чем `Physics2DBodyDestroyer.LateUpdate` может обработать `DestroyBodyRequest`. Тело оставалось в backend и collision queries.

**Результат:** `Physics2DBodyDestroyer` подписывается на `IEcsEntityEventListener` в `Init` и отписывается в `Destroy`. Синхронный `OnDelEntity` читает ещё доступный `PhysicsBodyRef`, инвалидирует handle и освобождает backend body. Тот же helper обслуживает обычный destroy request. Не нужны managed references в компонентах, static state, очереди или изменения сетевых callers.

**Затронутые места:**

- `Modules/Shared/Physics/Physics2D.Core/ECS/Physics2DBodyDestroyer.cs`
- `tests/Physics2D.Core.Tests/BodyCleanupTests.cs`
- `KarpikEngine.slnx` — новый тестовый проект.

**Acceptance criteria:**

- [x] Удаление entity с валидным `PhysicsBodyRef` синхронно вызывает `IPhysicsWorld2D.DestroyBody`, до следующего physics step.
- [x] Любой caller удаления entity, включая disconnect cleanup, использует тот же callback. Реальный Aether-тест проверяет исчезновение тела из backend и collision queries; сетевой transport smoke не требуется для этого контракта.
- [x] Cleanup не аллоцирует после warm-up: измеряется удаление 32 заранее созданных тел на реальном Aether backend.
- [x] Проверены pending request, повторное использование handle, отсутствие тела, invalid handle и отписка/повторная инициализация.

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

