# v0.5

## Главные изменения
* **Фундамент native memory:** Добавлен `Karpik.Memory` с явным владением unmanaged-памятью, borrowed views, caller-owned result handles, пулами фиксированной емкости, linear allocation и debug-диагностикой stale/invalid access.
* **No-GC jobs runtime:** Добавлен отдельный value-job `JobScheduler` для `IJob` и `IJobFor` с native descriptor storage, bounded worker queues, work stealing, dependency tracking, profiler hooks и `0 B` managed allocation на измеренных steady-state путях после warm-up.
* **ECS update scheduler:** Последовательный `ISystemUpdate` путь из `v0.4` заменен generated graph-based scheduler-ом. `ISystemUpdate` теперь parallel-by-default, если доступ к компонентам известен и не конфликтует.
* **Analyzer и codegen для scheduler-а:** Добавлены static scheduler metadata, Roslyn validation и generated per-assembly update registry, чтобы unsafe или opaque update systems fail-closed вместо случайного параллельного запуска.
* **Интеграция Runner-а:** Update scheduler встроен в `EngineRunner` с сохранением существующего Dragon wrapper ordering и DI behavior. Production mode использует `JobScheduler` value jobs; deterministic и single-thread modes остаются доступными.
* **Server fixed tick backlog:** Обработка server overload изменена: bounded catch-up diagnostics сохранены, но pending fixed-tick backlog больше не отбрасывается молча.

## Native Memory
* Добавлен проект `Karpik.Memory` и focused tests.
* Добавлено aligned native allocation ownership через `NativeAllocation`, `NativeMemoryDiagnostics` и generation tokens.
* Добавлены `NativeArray<T>`, `NativeSlice<T>`, `NativeResult<T>` и `NativeResultHandle<T>` для unmanaged storage и borrowed hot-path access.
* Добавлены `NativeLinearAllocator`, `NativePool<T>` и `NativeArena`.
* Добавлено debug-покрытие для invalid lengths, bounds failures, stale borrowed views, stale result handles, double dispose, double return и disposal invalidation.
* `Karpik.Jobs/SimpleNativeArray<T>` мигрирован на wrapper поверх `NativeArray<T>` с сохранением старой compatibility surface.
* Зафиксированы Release allocation measurements: preallocated `NativeArray<T>` traversal, `NativeLinearAllocator` allocate/reset, `NativePool<T>` rent/return и `NativeResult<T>` handle writes дают `0 B` managed allocation после warm-up.
* `NativeArena` отмечен как outside-frame allocator, потому что block growth/reset может аллоцировать managed metadata.

## Jobs
* Добавлены value-job contracts `IJob` и `IJobFor`.
* Добавлены API `JobScheduler`: `TrySchedule`, `TryScheduleParallel`, `Schedule`, `ScheduleParallel` и `Complete`.
* Добавлен `ValueJobHandle` поверх native descriptor identity со stale-handle invalidation.
* Добавлены native descriptor и payload storage, fixed dependency storage, pending completion checks, completed/failed generation tracking и cold-path exception reporting.
* Добавлен bounded native `WorkStealingDeque<T>` и worker publication через `TryPublish`, `TryRunNext`, owner pop, cross-worker steal, dependency requeue и capacity failure reporting.
* Добавлены worker runtime startup/shutdown, wake-up on publish/completion, stopped-publication rejection и volatile completion visibility.
* Добавлены optional profiler events через `JobProfilerEvent` и `JobBatchInfo`.
* Старый delegate-based `JobSystem` изолирован как allocating compatibility path и помечен `AllocatingCompatibilityAttribute`.
* Добавлены тесты, доказывающие, что `JobScheduler` не ссылается на legacy delegate runtime types: `JobSystem`, `JobHandle`, `JobWrapper`, `JobCompletion`, `CancellationTokenSource`, `ConcurrentQueue<JobWrapper>`.

## ECS Update Scheduling
* Добавлены scheduler metadata attributes: `[SequentialSystem]`, `[Reads<T>]`, `[Writes<T>]`, `[RunsAfter<TSystem>]`, `[RunsBefore<TSystem>]` и `[MainThreadOnly]`.
* Добавлены runtime scheduler contracts и graph artifacts в `Karpik.Engine.Core/Scheduling`: `IEcsUpdateRegistryProvider`, `EcsUpdateSystemDescriptor`, `EcsComponentAccessDescriptor`, `EcsSystemOrderDescriptor`, `EcsUpdateGraph`, `EcsUpdateGraphNode`, `EcsUpdateGraphBuilder`, `EcsUpdateGraphBuildException`, `EcsUpdateScheduler`, `EcsUpdateSchedulerMode`.
* Добавлен `EcsUpdateGraphBuilder`: он валидирует registered update systems против generated descriptors, упаковывает system/component types в dense IDs, строит conflict/order/sequential dependencies, находит explicit order cycles и сохраняет compact graph arrays.
* Добавлен `EcsUpdateScheduler`, который строит граф один раз при initialization и переиспользует его каждый кадр.
* Добавлено parallel execution через `JobScheduler` value jobs с preallocated handle и dependency buffers.
* Добавлены deterministic topological execution и single-thread registration-order execution modes для debugging и reproducible validation.
* `ISystemFixedUpdate` оставлен последовательным в `v0.5`; fixed simulation в этом релизе не параллелится.
* Старый Dragon `IEcsRunParallel` оставлен только как compatibility и baseline coverage. User assemblies по-прежнему блокируются от raw Dragon lifecycle APIs analyzer-правилами.

## Static Analysis и Code Generation
* Добавлены проверки `EcsUpdateSchedulerAnalyzer` для `ISystemUpdate.Update()`.
* Прямой `EcsPool<T>` access считается write access; `EcsReadonlyPool<T>` access считается read access.
* Source helper calls обходятся analyzer-ом, helper summaries валидируются.
* Metadata-only external helpers требуют явных `[Reads<T>]` или `[Writes<T>]` summaries.
* Delegate, dynamic, unresolved virtual/interface dispatch, reflection через `typeof(...)`, unsafe address-of, lifecycle facade calls и unsupported managed component summaries считаются opaque scheduled-update access.
* Reviewed BCL math calls остаются разрешенными.
* Добавлены reviewed world facade summaries для Dragon `EcsWorld.GetPool<T>()` и `GetPoolUnchecked<T>()`, а также wrapper `World.Get<T>()`, `Has<T>()`, `TryGet<T>()`, `Add<T>()`, `Set<T>()`, `Del<T>()` и `Event<T>()`.
* Добавлен aspect inference для `Where<TAspect>()`: поля `EcsReadonlyPool<T>` дают read access, поля `EcsPool<T>` дают write access.
* Добавлен `EcsUpdateRegistryGenerator`, который генерирует `IEcsUpdateRegistryProvider` на assembly из explicit scheduler metadata.

## Runner и миграция игры
* Scheduler contracts, generated registry contracts и graph builder перенесены в `Karpik.Engine.Core/Scheduling`, чтобы избежать project cycle `Runner -> ECS.Core -> Runner`.
* `EngineRunner` обновлен: он извлекает отсортированные `ISystemUpdate` instances из существующих Dragon `UpdateSystem` wrappers и инициализирует `EcsUpdateScheduler`.
* Frame execution обновлен: `EngineRunner` вызывает `EcsUpdateScheduler.Update()` вместо старого sequential update runner.
* Dragon wrapper layer/order sorting и DI behavior сохранены при миграции.
* Текущие opaque game update systems помечены `[SequentialSystem]`, включая ImGui/input/network/physics/lifecycle-facade style systems, пока их external APIs не будут reviewed и summarized.
* Server fixed-loop overload handling обновлен: pending fixed-tick backlog сохраняется, bounded catch-up diagnostics остаются.

## Проверка
* `dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -c Release -m:1 -nr:false` passed.
* `dotnet test ECS.Core.Tests\ECS.Core.Tests.csproj -m:1 -nr:false --no-restore` passed.
* `dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --no-restore` passed, включая parallel overlap, conflict serialization, deterministic order и `0 B` calling-thread allocation after warm-up.
* `dotnet test Tools\StaticAnalyzer.Tests\StaticAnalyzer.Tests.csproj -m:1 -nr:false --no-restore` passed, только с существующим `NU1900` vulnerability-feed warning из-за недоступного NuGet network access.
* `dotnet build ServerLauncher\ServerLauncher.csproj -m:1 -nr:false --no-restore` passed.
* `dotnet build ClientLauncher\ClientLauncher.csproj -m:1 -nr:false --no-restore` passed.

## Upgrade Notes
* `ISystemUpdate` systems scheduled by default. Если система выполняет thread-affine work, opaque external calls, lifecycle facade calls, reflection, unsafe aliasing, input, networking, ImGui или другие side-effect-heavy operations, пометьте ее `[SequentialSystem]`, пока access не будет явно summarized и reviewed.
* Для helpers и systems, чей ECS access не выводится напрямую, предпочитайте явные `[Reads<T>]` и `[Writes<T>]` metadata.
* Не используйте старый Dragon `IEcsRunParallel` path в game или module code. Используйте engine `ISystemUpdate` и scheduler metadata.
* Не используйте legacy delegate `JobSystem` APIs в новых hot paths. Используйте `JobScheduler` value jobs и caller-owned native storage.
* Не используйте `NativeArena` как steady-state frame allocator. Используйте preallocated `NativeArray<T>`, `NativeSlice<T>`, `NativeResult<T>`, `NativeLinearAllocator` или `NativePool<T>` где это уместно.
* `ISystemFixedUpdate` остается последовательным в этом релизе. Physics и fixed-step gameplay все еще должны использовать fixed dt и не должны рассчитывать на parallel fixed execution.
* Threaded client simulation/render pipeline не входит в завершенную foundation-часть. `ISystemRenderPrepare`, input snapshot/ring и Graphics.Core triple-buffer ownership остаются отдельным planned work.