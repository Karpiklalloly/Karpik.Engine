---
status: completed
complexity: complex
complexity_source: auto
development_mode: tdd
source_design: lua-hot-reload-design.md
design_revision: 10
plan_revision: 1
approved_plan_revision: 1
approved_design_revision: 10
---

# Заменять Lua-код без пересоздания Simulation

This ExecPlan is a living document maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Явный local-only host request приостанавливает Simulation, готовит новую версию всех Lua-модов с прежними схемами, проверяет scheduler-кандидат и переключает версии без потери ECS. После ошибки подготовки старая версия продолжает работать, если cleanup сохранил host integrity. Это code-only reload: не CLR/editor worker restart, не schema migration и не network reload.

Архитектурный источник: [готовый дизайн revision 10](lua-hot-reload-design.md), D1–D14, AC1–AC8. План предполагает единое утверждение до реализации; сохранённый ready design сам по себе её не разрешает.

## Progress

- [x] (2026-11-14) Восстановлены предыдущие решения, прочитаны runtime/host/scheduler/binding исходники, ADR и workflow skills.
- [x] (2026-11-14) Пользователь выбрал D2/D4–D12; technical ownership и boundary конкретизированы в D13/D14; design r10 сохранён ready.
- [x] (2026-11-14) Plan r1 проверен: все AC1–AC8 покрыты, WP-00..06 полны, зависимости ацикличны, ссылки и пути test projects существуют. Статус awaiting_approval.
- [x] (2026-11-14) Пользователь «Делай»: утверждены plan r1/design r10; status executing.
- [x] (2026-11-14) WP-00: fresh Release build 0 errors/52 warnings, targeted baseline 91/91, no skips. DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli, NUGET_PACKAGES=/home/artem/.nuget/packages. VSTest loopback blocked in sandbox; approved escalated run passed. Logs /tmp/karpik-reload-baseline-{build,test}.log. Host seams: worker reservation lease; snapshots must reject during maintenance; clocks must compensate.
- [x] (2026-11-14) WP-01 accepted: seven feature tests Red->Green; fresh feature/regression filter35/35 incl warmed allocation probes, no failures. Shared storage and revision-only retirement inspected. Logs /tmp/karpik-reload-wp01-*.log.
- [x] (2026-11-14) WP-02: lifecycle13/13 Green, включая async loader owner-thread и host-dispose phase failures. Initial9 Red->Green; дополнительные2 Red->Green. Общий feature59/59.
- [x] (2026-11-14) WP-03: root orchestration13/13 + real-file Static/Dynamic × Deterministic/Parallel switching/rejection, same world/entity generations, добавление/удаление Lua-систем; commit/retire без повторного CLR pipeline.Init.
- [x] (2026-11-14) WP-04: real ClientSimulationWorker boundary, worker9/9, timing3/3, async cancellation/shutdown/default-deny/Busy; first ticker Red Expected1 Actual2 исправлен. Latest build0errors/7existingwarnings; feature59/59 exit0, no skips (TRX11_01_47).
- [x] (2026-11-14) WP-05: feature62/62, full Runner702/702 under existing CI serialization (180s watchdog), Generator80/80, ECS.Core67/67, all no skips/exit0. Real-file300 requests/600 retired references, N0/1/16 resource probes, common warmed ticks0B. Reports/fingerprints under plans/probes/lua-hot-reload/.
- [x] (2026-11-14) WP-06: fresh independent read-only reviewer /root/independent_review — no actionable findings, no reviewer edits/test claims. API/ADR/evidence/probe/fingerprints/local PR text complete; git diff --check and links verified. D10 publication blocker reported; no remote mutation.

## Surprises & Discoveries

- `EcsUpdateScheduler.InitializeCore` вызывает DisposeSchedulerState до graph validation; активный объект переинициализировать нельзя. D8 требует отдельного кандидата.
- `ComponentQueryPreparer.Dispose` освобождает dynamic storage вместе с queries/commands; требуется разделить владельцев, сохранив native/dynamic pools.
- `PreparedComponentQuery` регистрирует world listener, `ClrComponentPool` вызывает GetPool: candidate preparation не полностью side-effect-free на инфраструктуре. Temporary listeners компенсируются; пустой pool допускается до world disposal, но gameplay values/membership не меняются.
- Клиент уже разделён на main-thread rendering и ClientSimulationWorker. Worker InvokeAsync сам по себе не исключает render access; нужен общий maintenance barrier.
- Time.IsPaused останавливает только TotalTime, FixedRunTicker и server loop зависят от wall clock. Простой флаг паузы не выполняет AC6.
- Начальные pipeline wrappers/registry удерживают LuaUpdateSystem; retiring версии должны терять сильные ссылки на VM/queries независимо от того, остаётся ли пустая infrastructure adapter оболочка до Simulation disposal.
- Branch exception D10: текущая codex/modding-0.7, task base fe018fd; parent features ещё не опубликованы. Нельзя автоматически расширять существующий PR.

## Decision Log

- 2026-11-14 / пользователь: все Lua-моды атомарно; lifecycle top-level/on_load/validation/on_reload/Commit, без повторного on_start. Остальные user decisions сохранены в source design, D2/D4–D12.
- 2026-11-14 / planning: complex auto сохраняется из-за runtime ownership, cross-thread barrier и двухфазной активации, TDD для observable behavior. Документы и baseline checks выполняются напрямую.
- 2026-11-14 / planning: Core определяет backend-neutral contracts, Runner оркестрирует boundary/scheduler, Lua владеет VM/candidate lifecycle. Runner project reference на Modding.Lua не добавляется.
- 2026-11-14 / planning: execution выбирает динамическую делегацию по реальным независимым пакетам; общий working tree, контроль overlapping edits/build outputs и обязательный отдельный read-only reviewer после интеграции. Здесь нет назначений агентов или фиксированных волн.

## Outcomes & Retrospective

Реализация WP-00..05 интегрирована: feature62/62, full Runner702/702 в репозиторной CI-сериализации, Generator80/80 и ECS.Core67/67. Warmed common tick0B; Lua experimental, reload costs отдельно в [evidence](lua-hot-reload-evidence.md). Решение владения записано в [ADR](../docs/02_ADR/lua-code-revision-lifetime.md). Свежий независимый reviewer /root/independent_review завершил read-only проверку без actionable findings. Все AC1–AC8 закрыты; plan completed. [Подготовленный PR](lua-hot-reload-pr.md) не опубликован из-за D10 scope blocker; push/PR не выполнялись.

После явного подтверждения пользователя «Добавь» сохранены четыре компактных pattern notes в `docs/knowledge/patterns`: storage/revision ownership, sim/render barrier, cleanup phases и Static/Dynamic optional DI parity. Существующая заметка о snapshots уточнена для revision lifetime. Runtime-код и приёмка не менялись.

## Context and Orientation

Рабочая директория для всех команд: `/home/artem/git/Karpik.Engine`. Ветка codex/modding-0.7 выбрана пользователем как явное исключение, не создавать новую ветку/worktree. До реализации перепроверить HEAD и worktree; сохранять чужие изменения. Новый task range начинается после fe018fd, не включает прошлые typed/structural-command features.

Persistent storage — ECS pools и компоненты, живущие до уничтожения Simulation. Revision — одна версия всех Lua VM, queries, handles/callback capabilities и bounded scratch; её уничтожение не уничтожает persistent storage. Candidate — подготовленная, ещё не активная revision с отдельным scheduler. Commit — короткая публикация согласованной пары revision/scheduler внутри закрытого host barrier. Retiring — обратная очистка старой версии после Commit, уже без права rollback.

Точки реализации:

- `Modules/Shared/Modding/Modding.Core/Bindings/ComponentQueryPreparer.cs`, `DynamicComponentStorage.cs`, `ComponentCallbackScope.cs`, `PreparedComponentQuery.cs`, `PreparedComponentHandle.cs`, `ComponentSnapshot.cs`, `StructuralCommandBuffer.cs`: storage/access lifetime.
- `Modules/Shared/Modding/Modding.Lua/LuaSimulationPreparation.cs`, `LuaScriptLoader.cs`, `LuaScriptRuntime.cs`, `ModsLifecycleManager.cs`, `Systems/LuaUpdateSystem.cs`, `Systems/UpdateSystem.cs`; common `ModContainer.cs`, `IModsLifecycleManager.cs`, `IScriptRuntime.cs`: lifecycle/preparation/routing.
- `src/Karpik.Engine.Core/LifeCycle/`, `IEngineRunner.cs`, `Bootstrap.cs`: neutral host/preparation contracts и forwarding, без Lua dependency.
- `src/Karpik.Engine.Core.Runner/Runner.cs`, `SystemRegistry.cs`, `WorkerHost.cs`, `FixedRunTicker.cs`, Core `ClientSimulationWorker.cs`, `Time.cs`: active graph, boundary, timing and shutdown.
- `src/Karpik.Engine.Core/Scheduling/EcsUpdateScheduler.cs` и first-party lifecycle bridge: менять только необходимое для candidate ownership/failure cleanup/reference release; не переписывать алгоритм scheduler и не менять third-party sources.

Имена новых private aggregate/factory types можно выбрать в execution. Предлагаемые Core contracts под `LifeCycle/`: simulation code reload request/result (typed status), participant подготовки и disposable candidate. Contract отделяет Load/Bind, финальную подготовку on_reload после graph validation, no-user-code activation и retirement. Host expose — явный async request с CancellationToken и local opt-in, не RPC. Исключения в protocol/host integrity диагностируются и останавливают Simulation; обычный preparation rejection возвращает структурированный результат. Разрешение backend-neutral participants выполняется существующим startup infrastructure путём, зависимости внутри сервисов внедряются конструкторами.

## Real-Time Assessment

Обычные RunGameplayFrame/Update/FixedUpdate/render loops затрагиваются только gate/routing: без новых allocations, locking, IO, graph build или делегатов на кадр. Проверяется нулевой warmed common overhead до и после reload и при отсутствии request; существующие Lua ceilings не изменяются. Maintenance allocations и workers допускаются только внутри явной паузы, измеряются отдельно. Две одновременно живые версии — максимум активная и кандидат; после commit старое освобождается до нового запроса.

ECS layout не меняется: struct components, dense native/dynamic pools, существующие prepared queries/ref access. Generation/version capabilities запрещают старый доступ; schema equality каноническая и полная, pointer registry/policy не меняются. Structural queues старой версии завершены до barrier и никогда не проигрываются повторно.

Мир/пулы/Time/CLR systems сохраняются; VM globals, callbacks, listeners, queries, command scratch и scheduler resources заменяются. Simulation не reparent в DI; ModSet definitions/schema registry фиксированы, Lua executable revision принадлежит Simulation factory-owned aggregate. Shared common слой без MoonSharp. Не добавлять Client reference в Server и наоборот.

Gameplay fixed dt неизменен, wall-clock reload time вычитается из client variable delta, client fixed ticker и local server deadlines с сохранением pre-pause остатка. Main-thread preparation требует остановленного simulation worker и исключённого render/frame-begin; control scheduler должен прокачиваться без reentrant ticks. Error path не принимает faulted job execution за completed safe boundary. Обычный render не работает во время maintenance; keep-rendering UI не входит в scope.

Нет нового network protocol, сообщений/сериализации или сетевого reload; host local-only policy default deny. Отмена кооперативна, зависший Lua остаётся известным ограничением, infinite-loop in-process tests запрещены.

## Plan of Work / Milestones

Каждый пакет выполняет test-first для новых проверяемых поведений, подтверждает ожидаемую причину Red, затем Green и targeted regressions. Before/after refactor нужны characterization tests; инфраструктура xUnit уже есть. Каждый пакет завершается собственной validation и записью команды/результата в Progress до зависимых изменений. Ниже пакеты не являются назначениями агентам.

### WP-00 — Зафиксировать baseline и seams

- Status: completed.
- Goal: воспроизводимый baseline, пригодный для сравнения reload.
- Scope: git/task range, существующие Lua lifecycle/query/command и scheduler/worker tests; определить точный startup DI/static composition путь для neutral participant и host opt-in, не менять продуктовый scope.
- Boundaries: без behavior implementation, без push предыдущих коммитов.
- Depends on: none.
- Acceptance: AC8, baseline для AC1–AC7.
- Verification: свежая smallest Runner build и targeted существующий baseline из Concrete Steps; записать SDK/configuration, counts и ограничения. Если HEAD/чужие edits изменили contracts, проверить только затронутые предпосылки.

### WP-01 — Разделить persistent storage и revision bindings

- Status: completed.
- Goal: disposable revision queries/capabilities/scratch без уничтожения ECS данных.
- Scope: common Bindings файлы из Context; constructor-injected persistent storage holder и revision factory/aggregate; Dispose не удаляет dynamic components/anchors; pointer policy/catalog остаются постоянными. Version-specific scopes/generations, bounded command buffers, temporary query listener compensation, new empty native pool policy. Сохранить существующие immediate/deferred semantics и allocation budgets.
- Boundaries: без Lua lifecycle hooks и host/scheduler switching; не менять component layout.
- Depends on: WP-00.
- Acceptance: AC1, AC2, AC3, AC4.
- Verification: common tests удерживают old handles/views/query/snapshots после generation retirement, включая тот же owner ID; candidate failure/Dispose не меняет entity membership/values; nested/fixed/pointer данные сохраняются. Повторные dispose/prepare не накапливают listeners/scratch. StructuralCommandBuffer/typed/composite/command allocation regressions.

### WP-02 — Подготовить Lua revision и lifecycle

- Status: completed.
- Goal: получить неактивного кандидата всех Lua-модов с правильным lifecycle и cleanup.
- Scope: neutral candidate/participant contracts в Core и constructor DI/static factories; factory-owned aggregate в Modding, split persistent/current revision в lifecycle manager, loader/runtime validation on_reload. Сохранять interfaces совместимыми где достаточно additive contract; если общие lifecycle interfaces меняются, обновить все локальные implementations/tests. Candidate load/bind и финальный on_reload отделены, чтобы runner мог проверить граф между ними. Candidate queries не доступны из hooks. Активация не вызывает Lua и сохраняет enabled flags. Legacy и declared callbacks читают единую active revision.
- Boundaries: без собственного host loop/scheduler, без metadata rescan/migration, без обновления VM globals из старой версии.
- Depends on: WP-01.
- Acceptance: AC2, AC3, AC7.
- Verification: real MoonSharp tests с несколькими модами, Shared/side и legacy callbacks. Порядок hooks; отсутствующий/не-function on_reload; ошибка top-level/on_load/on_reload; partial load; unload attempt даже при начатом failing on_load; rejected candidate без destroy, committed revision с destroy несмотря на пропущенный start. Cleanup продолжается после ошибок, host failures отличимы от Lua InterpreterException. Проверить no ECS access через retained capabilities, disabled flags и две независимые Simulation.

### WP-03 — Подготовить и опубликовать execution revision

- Status: completed.
- Goal: validated scheduler-кандидат с теми же CLR instances и новыми Lua adapters.
- Scope: Runner/SystemRegistry setup seams, neutral runtime descriptors, отдельный EcsUpdateScheduler InitializeInstances для кандидата, publish/retire protocol. Хранить baseline CLR systems/descriptors без накопления retired version roots; старый pipeline не удерживает VM через первоначальные adapters. Commit только при closed barrier из WP-04 (тестируется fake boundary до интеграции); active graph/version читаются согласованно. Candidate worker/GCHandle failure cleanup надёжен, старый scheduler не Dispose до successful Commit.
- Boundaries: не пересобирать/Init/Destroy весь pipeline и не менять scheduler ordering semantics. Без background preparation.
- Depends on: WP-02.
- Acceptance: AC1, AC2, AC3, AC5, AC7.
- Verification: новый граф добавляет/удаляет/переупорядочивает systems, меняет accesses/queries; cycle/unknown target/query/schema error оставляет старую версию. CLR instance identity и Init/Destroy counters неизменны. Candidate не исполняет callbacks до Commit. Проверить Dynamic/Static × Deterministic/Parallel, SingleThread smoke, legacy fixed/update routing, failure injection scheduler preparation/activation infrastructure.

### WP-04 — Закрыть host boundary и управлять reload time

- Status: completed.
- Goal: реально безопасный explicit async host API, пауза и корректное возобновление.
- Scope: Core request/result/opt-in forwarding, Runner coordinator, Bootstrap/WorkerHost, ClientSimulationWorker и FixedRunTicker/time seams. Зарезервировать request once; Busy без очереди; default deny/local-only не выводить из Side. Дождаться in-flight gameplay frame и jobs, исключить render/frame-begin, затем main-thread prepare/Commit/cleanup с control pump. Shutdown ждёт безопасной cleanup/cancellation, не освобождает используемые scopes; commit не прерывается cancellation. Компенсировать maintenance duration в host/client/server clocks без потери pre-pause fixed remainder.
- Boundaries: без editor UI/IPC protocol/watcher, keep-rendering режима и network reload; не смешивать с существующим worker-restart hot reload.
- Depends on: WP-03.
- Acceptance: AC2, AC3, AC6, AC7.
- Verification: deterministic event/barrier tests, а не timing sleeps. Запрос на worker/main/external thread не deadlock; in-flight frame/render resource access не перекрывается с prepare/dispose; faulted worker/request before startup/shutdown rejects; Busy, cancellation на стадиях, cleanup Lua/host failure до/после Commit, повторный shutdown. Контролируемый clock/fake time доказывает no inflated dt/fixed catch-up и сохранение пользовательской pause. Реальный ClientSimulationWorker и local Server host path.

### WP-05 — Проверить интеграцию, ресурсы и allocation gates

- Status: completed.
- Goal: end-to-end evidence всех AC и повторяемость reload.
- Scope: новые `tests/Karpik.Engine.Core.Runner.Tests/LuaHotReload*Tests.cs` и связанные fixture seams; relevant ECS/generator tests, standalone probe/evidence под plans. Fixture пишет реальные Lua-файлы и вызывает host API, сохраняет world/entity/data identity при изменении behavior. Многомодовый отказ доказывает отсутствие partial publish.
- Boundaries: не принимать предыдущие counts за новые, не ослаблять Lua ceilings; no NativeAOT/manual game/network acceptance без отдельного реального запуска.
- Depends on: WP-04.
- Acceptance: AC1–AC8 (AC8 review завершается WP-06).
- Verification: не менее 20 success/failure alternations с retained handles и GC/weak-reference probes вне hot loop; scheduler workers/GCHandles/listeners не растут. 100–300 seeded последовательностей success/reject/cancel/busy для orchestration invariants. Измерить N=0/1/16 Lua systems и небольшую/среднюю заданную ECS population (точные размеры записать), pause/prepare/commit/retire, managed allocation и peak live/process memory со временем sampling и ограничениями; no-request common warmed loop 0 B до/после. Full Runner/Generator/ECS.Core зелёные по свежим Release DLL; baseline/final counts из actual results.

### WP-06 — Независимое review и документация

- Status: completed.
- Goal: сверить реализацию с design/AC и сделать ограничения reviewable.
- Scope: после интеграции отдельный read-only reviewer изучает diff, contracts, failure/cancellation/thread/ownership/allocation evidence. Главный агент исправляет actionable findings и запускает затронутые rechecks. Обновить `docs/modules/modding/`, добавить ADR Lua code-revision ownership и уточнить `runtime-preparation-scopes.md`/общий modding design только в затронутых разделах. Сохранить evidence, task PR title/body и публикационный статус.
- Boundaries: reviewer не редактирует общий план; knowledge notes автоматически не записываются. Не добавлять changelog без отдельного versioning решения/skill.
- Depends on: WP-05.
- Acceptance: AC8; финальная сверка AC1–AC7.
- Verification: independent report без открытых actionable findings; relevant tests после исправлений; git diff --check и local links; prepared PR соответствует одному feature и действительному task range. Если isolated publication запрещена branch scope/remote prerequisites, записать blocker и title/body, не push текущей ветки автоматически. Предложить 3–7 кратких knowledge learnings после завершения, без автоматической записи.

## Concrete Steps

Все команды из `/home/artem/git/Karpik.Engine`. Ниже команды будущей validation, они ещё не выполнялись. Сначала свежая сборка; --no-build только после подтверждения актуальности DLL. Использовать существующий NuGet cache; --no-restore добавлять только после успешного restore/подтверждения assets. Если runner требует loopback permission, зафиксировать host restriction и разрешённый способ, не объявлять tests passed.

```bash
git status --short --branch
git rev-parse HEAD
dotnet build tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false
dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~LuaPreparationTests|FullyQualifiedName~EcsUpdateSchedulerRuntimeTests|FullyQualifiedName~ClientSimulationWorkerTests|FullyQualifiedName~StructuralCommandBufferTests' --blame-hang --blame-hang-timeout 60s --blame-hang-dump-type none
```

WP-01..04: после каждой свежей build выполнить соответствующий фильтр; имена новых suites ниже закрепить при добавлении tests, не запускать фильтр с нулём выбранных tests как успех.

```bash
dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~LuaHotReload|FullyQualifiedName~StructuralCommand|FullyQualifiedName~LuaComposite|FullyQualifiedName~LuaPointer' --blame-hang --blame-hang-timeout 60s --blame-hang-dump-type none
```

WP-05 global: использовать существующую `.github/runner-tests.xunit.json` (CI=true при сборке либо копия в Runner test output перед --no-build), исключая конкуренцию timing-sensitive process tests. Явно fresh build каждого project, затем тесты; можно использовать dotnet test без --no-build, чтобы собрать dependencies. Не запускать одновременные builds общей директории.

```bash
dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false --logger trx --blame-hang --blame-hang-timeout 180s --blame-hang-dump-type none
dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -c Release -m:1 -nr:false --logger trx
dotnet test tests/ECS.Core.Tests/ECS.Core.Tests.csproj -c Release -m:1 -nr:false --logger trx
git diff --check
```

Ожидаются exit 0, фактически выбранные tests > 0, failed 0; skipped tests отдельно описать. Warning baseline и новые warnings различать. Performance/resource probe command и workload фиксируются при создании harness в WP-05, с source/binary fingerprints; настоящий report нельзя заменить зеркалящим implementation тестом или helper-only benchmark.

## Validation and Acceptance

| Criterion | Primary packages | End-to-end evidence |
| --- | --- | --- |
| AC1 | WP-01, WP-03, WP-05 | Same world/entity/data after behavior change, two Simulation isolation |
| AC2 | WP-01..05 | Fail/cancel at preparation stages, unchanged old version, cleanup integrity |
| AC3 | WP-01..05 | Lifecycle counters, stale access, enabled flags, teardown once |
| AC4 | WP-01, WP-05 | 0 B common ticks, unchanged Lua gates, reload/peak resource probes |
| AC5 | WP-03, WP-05 | New graph + same CLR instances, composition/scheduler matrix |
| AC6 | WP-04, WP-05 | Actual client worker/server local path, Busy/default deny/cancel, time/barrier tests |
| AC7 | WP-02..05 | Multi-mod all-or-none, schema rejection, legacy/Shared/side routing |
| AC8 | WP-00, WP-05, WP-06 | Fresh regressions, independent reviewer, docs/ADR/evidence/publication status |

Compatibility: additive host API/on_reload; reload в hosts без explicit opt-in недоступен. Обычный initial on_start остаётся прежним; новая activated revision без on_start корректно участвует в shutdown. Прежние runtime/preparation clients и Static factories должны собираться; schema/network/save protocol и native component layouts не меняются.

## Idempotence and Recovery

Build/tests повторяемы; fixture snapshots/files временные и удаляются с finally. До Commit отказ кандидата освобождает только его ресурсы, старые storage/VM/scheduler живы; если host integrity не восстановлена, Simulation останавливается. Во время Commit пользовательский код не вызывается, отмена не применяется. После Commit старая версия не используется даже при Lua cleanup ошибке; host cleanup failure приводит к стопу, а не rollback. Повторные Dispose/shutdown не повторяют hooks и не освобождают shared pools.

Не устранять зависший callback освобождением зависимостей из другого потока; cancellation cooperative. Faulted jobs не считать barrier. Если обнаружится новый архитектурный конфликт, обновить только затронутый source design вопрос и план, получить scoped approval перед изменением public behavior; техническая перестановка пакетов без изменения D/AC не требует нового утверждения.

Не выполнять reset/clean/cherry-pick/rebase/push для обхода branch blocker. По D10 публикация общей ветки с прежними этапами не разрешена; подготовить isolated PR description и точно сообщить ограничения. Не считать отсутствие публикации отсутствием реализации, но и не писать, что PR создан.

## Artifacts and Notes

- Source: [design r10](lua-hot-reload-design.md).
- Standards: [PLANS](PLANS.md), [preparation ADR](../docs/02_ADR/runtime-preparation-scopes.md), existing [structural-command evidence](lua-structural-commands-evidence.md) как исторический reference.
- Outputs created: [evidence](lua-hot-reload-evidence.md), [probe/report/fingerprints](probes/lua-hot-reload/README.md), [API docs](../docs/modules/modding/lua-hot-reload.md), [ADR](../docs/02_ADR/lua-code-revision-lifetime.md), [prepared PR text](lua-hot-reload-pr.md). Actual verification and publication status are recorded above.
- Предлагаемый будущий PR title: `feat: Reload Lua code while preserving Simulation state`. Body после реализации должен описывать реальное поведение/verification/compatibility и limits local-only/unchanged schemas. Labels брать из реального repository setup, breaking changes отмечать явно.

## Review findings

- Final independent review: no actionable findings. Reviewer inspected actual tracked/untracked files against design r10/AC1–AC8, did not edit or rerun tests. Main agent accepted the report after final global checks.

## Adjustments

(2026-11-14) Финальная проверка: Static DI optional-null default восстановлен в RuntimeCompositionGenerator (existing composed-scope test Red77/78 -> Green80/80 с регистрацией/отсутствием scheduler). Shutdown frame gate Red Expected1 Actual2 -> Green; scope teardown ждёт in-flight frame. Logger failure during cleanup Red missing a:old:unload -> Green, остальные VM cleanup продолжаются. API drain переименован DrainCodeReloadForShutdownAsync, чтобы не смешивать необратимый shutdown с обычной отменой токеном. Все D/AC сохранены, отдельное утверждение не требуется.

(2026-11-14) Full Runner first watchdog60s interrupted existing MixedFive N4096 after498passed/0failed; rerun watchdog180s preserves all assertions/budgets. Default-parallel rerun exposed existing ProcessManagerLifecycle readiness10s timeout, Final CI-serialized full suite later passed702/702; the old readiness test also passed isolated1/1. No unrelated ProcessManager change. Implementation agents subsequently stopped with usage-limit errors; root integrated fixes. A fresh independent reviewer later completed final read-only review successfully.

(2026-11-14) Независимые infrastructure seams/tests WP-03/WP-04 подготовлены вместе с WP-01, без изменения D/AC/dependencies конечной приёмки. Combined runtime Red13 cases:3 root scheduler modes,1 worker lease,9 Lua lifecycle. Host worker tests не запускались полностью против stub, чтобы не вызвать ожидаемый self-join hang.

(2026-11-14) Execution начато по явному утверждению пользователя. Архитектура/AC не менялись. Остальная запись ниже относится к состоянию до утверждения.

Исходное состояние planning заменено выполнением выше. Утверждение plan r1/design r10 записано; повторное утверждение технических корректировок не требуется.
