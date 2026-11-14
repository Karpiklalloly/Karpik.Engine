# Транзакционный hot reload Lua при неизменных схемах

- Status: ready
- Started: 2026-11-13
- Updated: 2026-11-14
- Revision: 10
- Complexity: complex (auto)
- Development mode: tdd (auto; исследование и документы — normal)

## Goal and scope

По явному host-запросу заменить весь Lua-код текущего фиксированного ModSet одной транзакцией, сохранив Simulation, мир, сущности и компоненты. Можно добавлять/удалять Lua-системы, менять queries, доступы и порядок. Схемы, набор модов и CLR-код неизменны. Первый срез local-only: без сетевого согласования, editor UI, watcher, миграций, asset overrides и новых индивидуальных FixedUpdate adapters.

Базовый целевой контракт: [общий дизайн моддинга](script-modding-design.md). Этот срез конкретизирует code-only reload; полноценная замена ModSet с миграциями остаётся будущим этапом. `ready` означает готовность к planning, не утверждение реализации.

## Requirements and behavior

Host явно разрешает local-only reload; по Side.Client/Server нельзя определять наличие сетевой игры. По умолчанию reload запрещён. Запрос без поддержки/разрешения отклоняется без паузы и загрузки. Один запрос находится в работе; конкурентный запрос возвращает Busy, очереди нет. Выбор отдельного мода не предоставляется. Набор definitions/сторон/ID/version/dependencies из ModSet фиксирован; новые моды не сканируются, metadata не обновляется. Меняются Lua-файлы существующих модов, включая загружаемые ими Shared/side modules в пределах действующего loader policy.

Состояния операции: Idle -> Quiescing -> Preparing -> Commit -> Retiring -> Idle. До Commit отказ/кооперативная отмена приводит к очистке кандидата и возобновлению прежней версии; host cleanup failure ведёт в Faulted и останавливает Simulation. Отмена после входа в Commit не действует: завершаются переключение и cleanup. Повторный shutdown/dispose не дублирует lifecycle. Запрос до startup или при shutdown отклоняется. Busy охватывает весь период до завершения cleanup.

Пауза начинается после завершения полного текущего gameplay frame, ECS jobs, Lua callbacks и их structural commands, при исключённом одновременном render/frame-begin доступе. Во время подготовки обычные gameplay/render callbacks не запускаются. Host control/main-thread scheduler продолжает обслуживаться для завершения async preparation без deadlock, но не допускает reentrant tick/reload. Подготовка и переключение выполняются на main thread при остановленном simulation worker; кандидат не исполняет системы до Commit. Состояние существующей пользовательской паузы сохраняется. Время reload исключается из следующего variable dt, TotalTime и fixed catch-up; накопленный ДО reload остаток fixed времени сохраняется.

Порядок для каждого кандидата: top-level Lua -> on_load. После загрузки всех кандидатов проверяются все схемы, queries, доступы и общий граф с неизменными CLR-системами. Затем on_reload вызывается в стабильном порядке mod ID, Shared перед side. Hook необязателен и без аргументов; он настраивает только локальное состояние новой VM. ECS-capabilities кандидата не активны до Commit. Нет автоматического переноса globals/closures/coroutines. on_start повторно не вызывается, initial spawn не повторяется; activated-контейнер новой версии должен впоследствии корректно получить on_destroy даже без on_start. Enabled-флаги сохраняются по mod ID; выключенный мод не начинает обновляться из-за reload.

После последней проверки отмены Commit публикует согласованную пару runtime revision + scheduler при отсутствии наблюдателей. До возобновления кадров старые handles/snapshots с version-owned capabilities инвалидируются. Нельзя наблюдать новую VM со старым графом или старую VM с новым. Commit не вызывает Lua, файловую систему и других fallible пользовательских действий; allocation/подготовка выполняются заранее. Неожиданный инфраструктурный сбой Commit останавливает Simulation, не выдаётся за recoverable preparation failure.

После Commit у старой активной версии выполняются on_destroy и on_unload; ECS-доступ уже закрыт. Отклонённый кандидат получает попытку on_unload для каждой VM, чей on_load был начат, но не on_destroy; при ошибке самого top-level освобождается только созданный runtime/loader state. Cleanup идёт в обратном порядке и не прекращается после первой ошибки. Ошибки Lua cleanup логируются, host-ресурсы всё равно освобождаются. При успешной host-очистке новая версия продолжает работу; отказ освобождения host-ресурсов/остановки jobs останавливает Simulation. До Commit это же правило запрещает возобновлять старую версию при недоказанной host integrity. После Commit rollback к старой версии запрещён.

Top-level/on_load/on_reload и teardown не получают ECS или иной capability изменения сохраняемого gameplay state. Чтение Lua-файлов и диагностические логи не откатываются. Новые host effects/API этому срезу не добавляются; произвольные сторонние host bindings не получают обещания транзакционности. Синтаксическая/декларативная ошибка, schema mismatch, invalid order/cycle/access/query, исключение on_load/on_reload отменяют всю подготовку. Отмена кооперативна: синхронный зависший Lua callback не прерывается; этот срез не добавляет timeout, instruction budget или sandbox redesign.

## Architecture and integration

Persistent Simulation resources: ECS world/native pools, dynamic component storage, pointer registry/policy, immutable component catalog, Time и обычные CLR service/system instances. Ни один из них не освобождается вследствие Lua reload.

Lua revision resources: VM/ModContainers, declared adapters, queries/listeners, callback scope/capability generation, prepared handles и bounded command/payload/validation buffers. Revision — disposable aggregate, созданный constructor-injected factory; runtime instances создаются этой фабрикой как динамические данные, а не извлекаются service locator. Агрегат не владеет Simulation storage. Отдельная generation гарантирует запрет старого доступа даже при одинаковом system ID в новой версии. Подготовка queries не Capture/перечисление мира и не меняет компоненты.

ModSet остаётся неизменным владельцем definitions/schemas, Engine -> ModSet -> Simulation не меняется. Под ним допускаются последовательно активируемые Lua code revisions с тем же набором и схемами. Это уточнение исторического утверждения, что prepared programs принадлежат только ModSet: текущий code-only срез хранит executable Lua revision у Simulation. Альтернатива reparent Simulation под новый ModSet невозможна без её пересоздания. Альтернатива отдельного DI child scope для каждой версии добавляет lifecycle/Static composition поверхность без необходимости новых exports; выбран explicit aggregate с внедрённой фабрикой и детерминированным владением.

Сохраняется один активный scheduler и не более одного кандидата. Кандидат содержит те же экземпляры CLR Update-систем и новые Lua adapters. Он строится отдельно, проверяет полный граф, резервирует буферы и подготавливает workers; пользовательские системы не выполняются. Старый scheduler не переинициализируется. При Commit меняется согласованный active revision/execution state, затем старый scheduler освобождается. Job workers кандидата могут существовать одновременно со старыми только во время явной maintenance-паузы. Обычный pipeline и CLR Init/Destroy не повторяются. Legacy on_update/on_fixed_update читают ту же активную revision, что declared adapters; их routing не должен оставаться привязанным к старым контейнерам.

Core/Runner не получает project reference на Modding.Lua. Host/boundary и typed request/result contracts находятся в Core; backend-neutral preparation/candidate contract позволяет модулю предоставить подготовленные runtime systems и lifecycle через DI. Lua-типы не выходят из backend. Mutable активная версия не уходит в Engine/ModSet static state. Результат host-операции различает успех, Busy, unsupported/invalid state, отказ до Commit и отмену; fatal infrastructure failure явно останавливает Simulation и содержит диагностику. Не вводится сетевой/RPC контракт.

Инфраструктурные эффекты подготовки допустимы только без изменения gameplay state: новый пустой CLR pool может остаться до конца жизни world (по одному на зарегистрированный type), а временные query listeners обязаны удаляться при отказе/retirement. Схемы идентичны после канонизации: тот же набор ID, version, field names/kinds, nested trees, fixed length, enum underlying, pointer type IDs. Только перестановка объявлений полей при одинаковой канонической форме допускается. CLR binding catalog/pointer policy заморожены; удаление/добавление/изменение dynamic схем отклоняется.

## Verified project context

Контекст ниже зафиксирован по исходникам на fe018fd до реализации; исходный worktree был clean. Финальная реализация и выполненные проверки находятся в [ExecPlan](lua-hot-reload-execplan.md) и [evidence](lua-hot-reload-evidence.md).

- `src/Karpik.Engine.Core.Runner/Runner.cs`: startup preparation и pipeline Init; scheduler вызывается отдельно от основного pipeline. Scope teardown уничтожает Simulation, поэтому не используется для этого reload.
- `src/Karpik.Engine.Core/Scheduling/EcsUpdateScheduler.cs`: InitializeCore уничтожает старое scheduler state до graph build; повторный Initialize активного объекта не транзакционен. Update ждёт scheduled jobs на нормальном завершении; faulted execution не считается безопасным barrier.
- `src/Karpik.Engine.Core/ClientSimulationWorker.cs`, runner `WorkerHost.cs`: simulation worker и main-thread render могут перекрываться. Нужен host barrier обеих сторон, а не только InvokeAsync в worker.
- `src/Karpik.Engine.Core/Time.cs`, runner `FixedRunTicker.cs`: IsPaused не останавливает callbacks, ticker использует Stopwatch; нужна компенсация maintenance time.
- `LuaSimulationPreparation.cs`, `ModsLifecycleManager.cs`, `ModContainer.cs`: startup-only registration/loading и on_start gate, reload lifecycle отсутствует.
- `Bindings/ComponentQueryPreparer.cs`: queries, command buffer и dynamic storage освобождаются вместе; владение нужно разделить. `PreparedComponentQuery` регистрирует world listener, `ClrComponentPool` вызывает GetPool. `ComponentSchemaRegistry`/`SchemaTree.Same` уже канонизируют и полно сравнивают требуемую форму.
- `first-parties/DragonECS.Karpik.Extensions/LifeCycleBridge.cs`: UpdateSystem удерживает исходный ISystemUpdate; pipeline/registry не должны удерживать retired VM через первоначальные adapters.
- [ADR подготовки](../docs/02_ADR/runtime-preparation-scopes.md) не предоставляет API изменения работающего графа. `HotReloadHandler` — .NET metadata notification, не Lua transaction coordinator.

Graph `.ua/meta.json` от 2026-11-01 на 7224b18 использован лишь как locator Runner.cs; факты проверены по текущим исходникам. QMD executable/connector не обнаружен; документы прочитаны напрямую. Указанная architecture skill ссылка `docs/01_Architecture/dependency-injection-and-scopes.md` отсутствует.

## Constraints

Обычный common tick: 0 новых B, без IO, locks, closures/LINQ, поиска имён, сборки графа или выделения revision wrappers. Dense pools/SoA и prepared ref access сохраняются. Lua остаётся experimental с прежними allocation ceilings; перенос startup/reload allocation в тик запрещён. Пауза, cold allocation, peak live memory, worker count и cleanup costs измеряются отдельно; SLA на время Lua не обещается. Повторные успехи/отказы не накапливают listeners, VM, worker threads, GCHandles или retained payload references. Не вводить Unsafe/SIMD ради maintenance orchestration. Client/Server/Shared separation и fixed dt сохраняются. Third-party DragonECS/MoonSharp sources не менять.

## Decisions and alternatives

- D1 — сохранить Simulation/ECS; restart-state/reсоздание Simulation не удовлетворяет цели.
- D2 — пользователь: Lua code-only, неизменные схемы; миграции отдельным этапом.
- D3 — workflow complex auto, TDD для behavior, normal для документов/исследования; одна approval gate для готового плана.
- D4 — пользователь: разрешены изменения Lua systems/queries/access/order; bodies-only слишком ограничивает reload.
- D5 — пользователь: отдельный необязательный on_reload до Commit без ECS; on_start не повторяется.
- D6 — пользователь: явная пауза вместо фоновой подготовки; цена — измеряемая остановка кадров, проще ownership/thread safety.
- D7 — пользователь: Lua cleanup error логируется, host cleanup failure останавливает Simulation; rollback после Commit запрещён.
- D8 — пользователь: отдельный scheduler-кандидат; общий executor с заменяемым state экономит peak resources, но требует более глубокого scheduler refactor и отложен.
- D9 — пользователь: manual host API/local-only, Busy, cancellation before Commit; без UI/watcher/network reload.
- D10 — пользователь: продолжать codex/modding-0.7 как явное исключение. Task base fe018fd; автоматически публиковать prerequisite commits/расширять PR #101 не разрешено.
- D11 — пользователь: все Lua-моды одной транзакцией; selective reload усложняет mixed revisions.
- D12 — пользователь: top-level -> on_load -> validate -> on_reload -> Commit; rejected candidate unload-only; retired active destroy/unload без ECS.
- D13 — техническое уточнение: persistent storage + factory-owned revision aggregate под Simulation, отдельные capabilities/scratch; смена ModSet parent нарушает D1, новый DI scope необязателен. См. Architecture and integration.
- D14 — техническое уточнение: пауза всего frame access, кооперативная отмена, immutable schema comparison и обратная best-effort cleanup конкретизируют D1/D6/D7/D9/D12; новые product функции не добавлены.

## Acceptance criteria

- AC1 — успешный reload меняет поведение Lua при той же Simulation/world, тех же entity IDs/generations и сохранённых CLR/dynamic/nested/fixed/pointer данных; две Simulation не разделяют mutable revisions/storage.
- AC2 — синтаксис, load/reload hook, schema mismatch, query/order/cycle/access ошибки и pre-Commit cancellation не меняют active version/gameplay data. После успешной cleanup старая версия продолжает работу; host cleanup failure останавливает её.
- AC3 — initial spawn/on_start не повторяются; принятый lifecycle соблюдён, enabled-флаги сохранены; старые handles/views/query/snapshots с access capabilities запрещены даже при одинаковом system ID. Rejected/retired resources очищаются ровно один раз по установленной политике.
- AC4 — common warmed tick 0 B без pending request до и после reload; прежние Lua allocation gates не ослаблены. Отдельно записаны pause duration, prepare/commit/retire time, cold allocations, peak live memory и workers для declared workloads; repeated reload не накапливает ресурсы.
- AC5 — добавление/удаление Lua systems и изменение queries/access/order отражается в новом общем графе с теми же CLR instances, без повторных CLR Init/Destroy; цикл/недоступный order target отменяет кандидата. Проверены Dynamic/Static composition и Deterministic/Parallel scheduling, smoke SingleThread с сохранением его существующей семантики.
- AC6 — host API: local opt-in, unsupported/default deny, Busy, pre-start/shutdown rejection, cancellation before Commit, no rollback after Commit. Во время паузы нет overlapping sim/render/job accesses, время reload не попадает в dt/fixed backlog; прежняя pause state сохранена. Проверен реальный client worker path и server-side local host path.
- AC7 — все Lua-моды переключаются вместе; частичный отказ не публикует часть VM; Shared/side loading и legacy update/fixed callbacks используют одну active revision. Нет изменений схем, mod metadata/set, network/save/assets/CLR reload scope.
- AC8 — интеграционные regressions, lifecycle/fault injection и resource-release checks проходят; после интеграции проведено независимое read-only review и устранены actionable findings. API docs/ADR/evidence обновлены, публикация соответствует D10 и точно описана без ложного PR claim.

## Open questions / blockers

None. Q1/Q2 -> D2/D4; Q3a -> D8; Q3b -> D13; Q4 -> D5; Q4b -> D7; Q4c/Q8 -> D12/D14; Q5a/Q5b -> D6/D9/D14; Q6 -> D10; Q7 -> D11. Это завершённый design, проверки реализации ещё не выполнялись.

## Resume point

Реализация завершена по [ExecPlan r1](lua-hot-reload-execplan.md), утверждённому пользователем для design r10. Runner702/702, Generator80/80, ECS.Core67/67, independent read-only review без actionable findings. [PR-текст](lua-hot-reload-pr.md) подготовлен локально; публикация ограничена D10, remote mutations не выполнялись. Revision10 требований/архитектуры не менялась; это обновление recovery checkpoint.
