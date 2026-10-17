# Подготовить интеграцию скриптовых модов

This ExecPlan is a living document maintained according to `plans/PLANS.md`.

## Текущее состояние

- Статус: запланировано.
- Сейчас: решения обсуждения записаны; реализация не начата.
- Далее: проверить возможности используемой Lua VM и расширение регистрации экземпляров.
- Блокеры: нет для исследования; механизмы отмены, миграций и DI переключения не доказаны.

## Purpose / Big Picture

Подготовить проверяемый путь реализации [согласованной спецификации](script-modding-design.md).
Первый объём включает Lua, публичные runtime-компоненты, системы в штатных
раннерах, asset overrides и транзакционный hot reload. WASM и переменные
коллекции отложены. Нельзя превращать этот объём в один release-wide PR.
Этот план начинает с технических проверок, после которых фиксируются точные
API и отдельные implementation milestones. Он не является разрешением писать
весь runtime сейчас.

## Progress

- [x] (2026-10-06) Записаны текущие договорённости и отличия от предыдущих требований.
- [ ] T1: Проверены Lua execution, память, отмена, isolation и AOT boundaries.
- [ ] T2: Проверен путь регистрации экземпляров адаптеров и scheduler identity.
- [ ] После T1/T2: составлены точные планы реализации по независимо проверяемым задачам.
- [ ] Реализованы storage/query/access и Lua bindings, проверены allocation budgets.
- [ ] Реализованы overrides и lifecycle/reload, проверена транзакционность.
- [ ] Проверены save/network интеграция и согласованный сетевой reload.

## Surprises & Discoveries

- Предыдущие требования включают коллекции, исключают reload первого объёма
  и отключают только ошибочную систему. Новая спецификация явно заменяет эти решения.
- В текущем checkout `Directory.Packages.props` закрепляет MoonSharp 2.0.0.
  Сам факт зависимости не доказывает отмену, лимит памяти или NativeAOT bindings.
- `SystemRegistry.Add<TSystem>` и `EcsUpdateGraphBuilder.Build` отвергают
  повторные CLR types. Dragon builder допускает неуникальные экземпляры.

## Decision Log

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

Созданы документы, код runtime не менялся. Проверки выполнения, AOT, allocations
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
