# Подготовить интеграцию скриптовых модов

This ExecPlan is a living document maintained according to `plans/PLANS.md`.

## Текущее состояние

- 2026-10-23: завершены L1–L5 из [подробного плана](lua-system-registration-remaining.md#реализация-2026-10-23):
  отдельные Lua Update-системы; callback — функция; смешивание с `on_update` запрещено.
  Сейчас: локальный завершённый этап; Rebuild 0 ошибок, Runner 215/215,
  ECS 49/49, generator parity/factories 4/4; независимое review без P1/P2.
  Далее: согласовать ECS bindings/queries; индивидуальный FixedUpdate отложен.
  Ручная игра и NativeAOT не проверялись. Публикация остаётся отдельным вопросом ниже.

### Предыдущий этап: 2026-10-22

- Статус на 2026-10-22: подготовка scopes завершена локально; общий моддинг частично реализован.
- Сейчас: разделены `ISystemRegistry` и `ISystemRuntimeRegistry`, добавлены
  `IModSetPreparation` и `ISimulationPreparation` с constructor injection;
  каталог перенесён в ModSet, загрузка runtime остаётся в Simulation.
- Проверено: Rebuild без ошибок; весь Runner-набор 189/189, review и исправления P1 завершены.
- Далее: объявления Lua-систем, отдельные Update-адаптеры и сквозная проверка.
- Блокеры реализации: нет. Публикация требует решения по ветке: текущая ветка уже
  связана с PR #101 про cooked assets; отдельная дочерняя ветка требует явного разрешения.
- Актуальная запись для продолжения: [что осталось сделать](lua-system-registration-remaining.md).
- Загрузка модов больше не зависит от позднего InitAsync; отдельные Lua-системы
  ещё не реализованы. MoonSharp сохранён.

Остальные разделы ниже сохраняют исходный исследовательский план от 2026-10-06;
они не являются актуальным отчётом о реализации. Текущие пути, коммиты,
проверки и оставшиеся шаги перечислены в записи для продолжения выше.

## Purpose / Big Picture

Подготовить проверяемый путь реализации [согласованной спецификации](script-modding-design.md).
Первый объём включает Lua, публичные runtime-компоненты, системы в штатных
раннерах, asset overrides и транзакционный hot reload. WASM и переменные
коллекции отложены. Нельзя превращать этот объём в один release-wide PR.
Этот план начинает с технических проверок, после которых фиксируются точные
API и отдельные implementation milestones. Он не является разрешением писать
весь runtime сейчас.

## Progress

- [x] (2026-10-23) L1–L5: Lua declarations/function callbacks, validation,
  runtime struct adapters, explicit component metadata, lifecycle и legacy compatibility.
  Rebuild: 0 ошибок, 46 предупреждений. Runner 215/215, ECS.Core 49/49,
  generator Client/Server parity и typed factories 4/4. Allocation comparison:
  1000 adapter/runtime вызовов выделяют столько же, сколько 1000 прямых MoonSharp;
  disabled adapter — 0 байт. Review без P1/P2; ручная игра/AOT не запускались.

- [x] (2026-10-22) P1–P3: этапы подготовки, runtime-контракт и Lua-перенос;
  `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~Preparation|FullyQualifiedName~RegisteredInstanceRunnerTests|FullyQualifiedName~AutofacCompositionTests|FullyQualifiedName~StaticCompositionTests|FullyQualifiedName~EcsUpdateSchedulerRuntimeTests|FullyQualifiedName~EcsRenderPrepareSchedulerRuntimeTests|FullyQualifiedName~EngineRunnerLifecycleTests|FullyQualifiedName~BootstrapBoundaryTests" --blame-hang --blame-hang-timeout 30s --blame-hang-dump-type none`: 69/69.
- [x] (2026-10-22) P4: `dotnet build tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -t:Rebuild --no-restore -m:1 -nr:false` — 0 ошибок, 44 предупреждения;
  `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj --no-build --no-restore -m:1 -nr:false --blame-hang --blame-hang-timeout 30s --blame-hang-dump-type none` — 189/189.
  Независимое review: два P1 исправлены с RED->GREEN регрессиями; иных важных замечаний нет.

- [x] (2026-10-06) Записаны текущие договорённости и отличия от предыдущих требований.
- [ ] T1: Проверены Lua execution, память, отмена, isolation и AOT boundaries.
- [ ] T2: Проверен путь регистрации экземпляров адаптеров и scheduler identity.
- [ ] После T1/T2: составлены точные планы реализации по независимо проверяемым задачам.
- [ ] Реализованы storage/query/access и Lua bindings, проверены allocation budgets.
- [ ] Реализованы overrides и lifecycle/reload, проверена транзакционность.
- [ ] Проверены save/network интеграция и согласованный сетевой reload.

## Surprises & Discoveries

- 2026-10-23: class LuaUpdateSystem конфликтовал с правилом generated DI:
  все классы ECS-систем должны быть public DI-кандидатами. Runtime-адаптер
  сделан readonly struct с единственным boxing при AddInstance; AttributeTargets
  SequentialSystem расширен на Struct. Generator parity прошёл RED→GREEN.

- 2026-10-22: независимое review нашло два риска новой async-подготовки:
  сборка pipeline после await могла уйти с main thread; синхронный Setup мог
  освободить scopes, пока подготовитель ещё работает. Регрессии воспроизвели
  неверный thread ID и преждевременный Dispose. Исправлено через существующий
  MainThreadScheduler, кооперативную отмену и отложенную очистку.
- 2026-10-22: оба host-а ждали Startup без прокачки scheduler. Добавлен
  RunUntilCompleted и обновлены CoreRunner/WorkerHost, иначе возврат startup
  на main thread мог заблокировать запуск.

- Предыдущие требования включают коллекции, исключают reload первого объёма
  и отключают только ошибочную систему. Новая спецификация явно заменяет эти решения.
- В текущем checkout `Directory.Packages.props` закрепляет MoonSharp 2.0.0.
  Сам факт зависимости не доказывает отмену, лимит памяти или NativeAOT bindings.
- `SystemRegistry.Add<TSystem>` и `EcsUpdateGraphBuilder.Build` отвергают
  повторные CLR types. Dragon builder допускает неуникальные экземпляры.

## Decision Log

- 2026-10-23 / разработчик: `systems` содержит отдельные объявления, `callback`
  принимает функцию; запретить смешивание с `on_update` на уровне всего мода.
- 2026-10-23 / Codex: использовать явный типизированный реестр имён компонентов,
  чтобы не добавлять reflection-discovery в Static. Общий lifecycle-gate остаётся
  в ModContainer; адаптеры не владеют VM. Реализация разрешена сообщением «Делай».

- 2026-10-22 / разработчик: разделить `ISystemRegistry` и `ISystemRuntimeRegistry`,
  вводить runtime-реестр через конструктор; добавить подготовку ModSet и Simulation;
  перенести каталог в ModSet. Реализация реестра остаётся общей.
- 2026-10-22 / Codex: после await создавать child scope, вызывать подготовителей
  и строить pipeline через main-thread scheduler. Sync Setup при незавершённой
  подготовке отменяет запуск и запрещает retry до окончания безопасной очистки.
  Цена: host обязан прокачивать scheduler во время ожидания Startup.

- 2026-10-06 / разработчик: нет обязательного бюджета времени/инструкций;
  ограничения памяти и команд отдельных ресурсов сохраняются.
- 2026-10-06 / разработчик: все модовые компоненты публичны, сторона — каталог.
- 2026-10-06 / разработчик: существующие раннеры + экземпляры адаптеров;
  горячая перезагрузка включена сразу, gameplay state принадлежит Simulation.
- 2026-10-06 / Codex: использовать текущую task-ветку `codex/modding-0.7`
  для связанных документов; не создавать дочернюю ветку для будущего runtime.
- 2026-10-06 / Codex: подробный план ограничен подготовительными проверками;
  не выдумывать API полного runtime до проверки интеграции.

## Outcomes & Retrospective

2026-10-23: отдельные Lua Update-системы работают в штатном pipeline;
согласованный формат и пример записаны в [дизайне](script-modding-design.md#стороны-и-объявления).
Это metadata/scheduling milestone, не реализация ECS-data bindings, hot reload или AOT.

2026-10-22: этап подготовки scopes реализован; индивидуальные Lua-системы ещё
не реализованы. Решение: [ADR подготовки](../docs/02_ADR/runtime-preparation-scopes.md).
Проверен реальный мод с `mod_info.json`, Shared/main.lua и MoonSharp callbacks:
load/start/update/destroy/unload вызываются по одному разу. Это автоматический
тест загрузки, не ручной запуск игры или NativeAOT publish.

Исходный результат 2026-10-06: созданы документы, код runtime не менялся. Проверки выполнения, AOT, allocations
и reload ещё не запускались. Документы не являются доказательством этих свойств.

## Context and Orientation

`Modules/Shared/Modding/Modding.Core/IModManager.cs` задаёт старые Init,
LoadMods, ReloadAllMods, StartMods, UpdateMods. Lua manager/container находятся
в `Modules/Shared/Modding/Modding.Lua/`; `ModdingLuaModule.cs` регистрирует
старые InitSystem и UpdateSystem. `Karpik.Engine.Core.Runner/Builder.cs`
превращает Karpik lifecycle интерфейсы в Dragon adapters.
`Karpik.Engine.Core/Services/ISystemRegistry.cs`, runner `SystemRegistry.cs`
и `Karpik.Engine.Core/Scheduling/EcsUpdateGraphBuilder.cs` — точки интеграции
runtime экземпляров. Существующее исследование:
[script-modding-investigation.md](script-modding-investigation.md).

ModSet означает неизменяемую подготовленную версию определений. При reload
Simulation переключается на новую версию после проверки, сохраняя совместимые
данные; новая версия не должна освобождать parent services работающей Simulation.
Точный способ интеграции этого переключения с DI требует исследования.

## Real-Time Assessment

Runtime затрагивает все ECS фазы, сеть и сериализацию. Мост не должен выделять
память в тике: разрешение имён/queries и allocation capacity выполняются заранее.
Data layout — dense SoA + sparse lookup; чтение/запись штатных компонентов через
bindings. Gameplay использует fixed dt. Первоначально моды последовательны;
структурные команды применяются на барьерах. Нельзя удерживать views после пачки.
Рост памяти и reload выполняются вне тика; пауза reload измеряется отдельно.
Расходы VM измеряются отдельно от моста. Отсутствие бюджета исполнения означает,
что пользовательский код не гарантирует предсказуемую длительность тика.
Client/Server разделены каталогами и API, server authoritative; сеть использует
общие схемы, batching и штатный transport, локальные handles не отправляются.

## Plan of Work

Подробные проверочные шаги: [T1/T2](../docs/superpowers/plans/2026-10-06-script-modding.md).
Сначала проверить MoonSharp и реальные calls ModContainer. Затем проследить
registration → Builder → runners → graph, зафиксировать минимальное расширение
экземпляров без нового scheduler. По результатам определить concrete API
storage, bindings, reload и миграций. После согласования создавать отдельные
задачи реализации из актуальной integration branch, с тестами каждого milestone.

## Milestones

T1 готов, когда записаны проверенные возможности VM и ограничения отмены,
memory enforcement и AOT; неизвестные свойства не названы поддержанными.
T2 готов, когда найден путь двух одинаковых adapters через все фазы, описаны
identity/order/access изменения и выполнены существующие regression tests.
После T1/T2 план дополняется конкретными signatures и acceptance tests для
runtime milestones. Полный дизайн не считается реализованным после исследования.

## Concrete Steps

Рабочий каталог: корень активного checkout KarpikEngine.

`dotnet build Modules/Shared/Modding/Modding.Lua/Modding.Lua.csproj -m:1 -nr:false`

`dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~EcsUpdateSchedulerRuntimeTests|FullyQualifiedName~EcsRenderPrepareSchedulerRuntimeTests|FullyQualifiedName~AutofacCompositionTests"`

`dotnet test ECS.Core.Tests/ECS.Core.Tests.csproj -m:1 -nr:false --filter FullyQualifiedName~EcsUpdateGraphBuilderTests`

Ожидание: успешная сборка и ноль failed tests. Эти команды пока не выполнялись.
Они дают baseline, а не доказывают поддержку модовых экземпляров или Lua cancellation.

## Validation and Acceptance

Все критерии перечислены в спецификации. Runtime milestones обязаны добавить
проверки конкретного поведения: mixed queries, ownership/handle generations,
readonly enforcement, stable IDs round-trip, repeated reload и отказ до commit,
две Simulation, asset conflict и network agreement. AOT publish должен реально
выполнить bindings, простого успешного publish недостаточно. Бесконечный скрипт
не запускается в процессе test runner; используется отдельный worker + watchdog.
Результаты и размеры workloads записываются в оба плана.

## Idempotence and Recovery

Документирование и baseline tests повторяемы. Не публиковать весь runtime одним
PR и не коммитить посторонние изменения. Ошибка подготовки reload не изменяет
активную версию. Неудачная миграция не портит исходные данные. После commit
старые подписки/ресурсы освобождаются один раз. Если безлимитный скрипт завис,
worker останавливается; не обещать сохранение последних изменений или бесшовный
rollback runtime-ошибки.

## Artifacts and Notes

- [Спецификация](script-modding-design.md).
- [Подробные задачи](../docs/superpowers/plans/2026-10-06-script-modding.md).
- Предыдущие требования и исследование сохранены с явной ссылкой на уточнения.
- Knowledge notes и memory не записывались. ADR handoff — после проверки реализации.
