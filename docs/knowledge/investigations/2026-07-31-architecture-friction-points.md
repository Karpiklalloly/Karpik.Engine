---
title: "Architecture friction points"
date: "2026-07-31"
tags:
  - knowledge
  - investigation
  - architecture
  - technical-debt
---

# Architecture Friction Points

## Question

Какие части текущей архитектуры KarpikEngine неудобны в использовании и сопровождении, если не учитывать редактор и лаунчер?

Это не принятые архитектурные решения и не готовый план рефакторинга. Документ фиксирует текущие точки трения, чтобы разбирать их постепенно. Перед существенным изменением следует подготовить отдельный ADR или ExecPlan и проверить актуальность наблюдения по коду.

## Context

В первую очередь затронуты:

- `Karpik.Engine.Core/Services`;
- `Karpik.Engine.Core/LifeCycle`;
- `Karpik.Engine.Core.Runner`;
- `DragonExtensions`;
- модульные installer API;
- ECS pipeline и scheduler integration;
- общая структура проектов.

Главный фильтр для будущих решений: предсказуемый lifecycle, явные зависимости, отсутствие аллокаций в hot paths и сохранение границ Client / Server / Shared.

## Findings

### 1. DI скрывает обязательные зависимости

Текущий паттерн:

```csharp
[DI] private Foo _foo = null!;
```

позволяет создать объект в невалидном состоянии. Если сервис не зарегистрирован, поле остаётся `null`, а ошибка возникает позже и не в composition root.

Дополнительные проблемы текущей реализации:

- reflection выполняется при каждом внедрении;
- сканируются в том числе `static`-поля и свойства;
- `OnInjected` ищется по имени метода, а не вызывается через строгий контракт;
- `Create<T>()` и `Inject()` имеют разную семантику post-injection;
- `IServiceContainer` проникает в прикладной код и превращается в service locator;
- обязательность и lifetime зависимости не видны из конструктора типа.

Предпочтительное направление:

- constructor injection для обычных сервисов и систем;
- фабрики для динамически создаваемых loader/saver и подобных объектов;
- отдельный узкий механизм для ECS world/pool injection, если он требуется DragonECS;
- отсутствие `IServiceContainer` в gameplay-коде;
- тяжёлая инициализация остаётся в `Init()` и других явных lifecycle-методах, а не в конструкторах.

Статус: [ ] Не разобрано

### 2. `ServiceProvider` слишком слабый и слишком открытый

Проблемы:

- наружу выставлен изменяемый `ConcurrentDictionary<Type, List<object>>`;
- `ConcurrentDictionary` не делает вложенные `List<object>` потокобезопасными;
- регистрация использует неатомарный `ContainsKey` с последующей записью;
- экземпляр может регистрироваться одновременно по интерфейсу и concrete type без связанного lifetime;
- `Forget<T>()` удаляет только один alias;
- `Get<T>()` печатает сообщение и возвращает `null` вместо fail-fast поведения;
- `GetAll<T>()` выполняет `Cast<T>().ToArray()`, хотя возвращаемый `Span<T>` создаёт впечатление allocation-friendly API;
- `InjectAll()` способен повторно вызвать post-injection callbacks.

Желаемый результат: небольшой закрытый composition container с явной фазой построения, проверкой всех обязательных зависимостей до запуска и без доступа к изменяемому внутреннему хранилищу.

Статус: [ ] Не разобрано

### 3. Слишком много lifecycle-диалектов

Сейчас одновременно существуют:

- `ISystemInit`;
- `ISystemBegin`;
- `ISystemMainThreadBegin`;
- `ISystemMainThreadFrameBegin`;
- `ISystemFixedUpdate`;
- `ISystemUpdate`;
- `ISystemLateUpdate`;
- `ISystemRenderPrepare`;
- `ISystemRender`;
- `ISystemDestroy`;
- соответствующие DragonECS interfaces и runners;
- wrapper-класс почти для каждой фазы.

`LifeCycleBridge` в основном состоит из повторяющихся адаптеров, а `Builder.Add(object)` содержит ручной type switch. Если система реализует несколько lifecycle-интерфейсов, она неявно превращается в несколько wrapper-объектов.

Желаемый результат: одна понятная модель lifecycle. Karpik-системы должны быть либо полноценной основной моделью, либо тонким адаптером над DragonECS, но не параллельной иерархией с большим bridge-слоем.

Статус: [ ] Не разобрано

### 4. `Runner` является god object

`Karpik.Engine.Core.Runner/Runner.cs` содержит около 700 строк и одновременно:

- владеет DI;
- сортирует и активирует модули;
- строит ECS pipeline;
- выполняет injection;
- конфигурирует update и render-prepare schedulers;
- управляет кадром;
- обрабатывает state для reload;
- строит runtime snapshot;
- уничтожает подсистемы.

Это создаёт слишком много причин для изменения одного класса и делает его центральной точкой связанности.

Предполагаемые границы разделения:

- `ModuleGraph`;
- `CompositionRoot`;
- `PipelineFactory`;
- `RuntimeLoop`;
- `StateSnapshotService`.

Точные границы должны быть подтверждены отдельным дизайном: механическое дробление класса без переноса ответственности проблему не решит.

Статус: [ ] Не разобрано

### 5. Module API имеет сильную temporal coupling

Installer может участвовать в нескольких фазах:

- `OnRegisterServices`;
- `OnConfigure`;
- `OnAnotherModuleLoaded`;
- `OnConfigureComplete`;
- `Destroy`;
- reload hooks.

Корректность модуля зависит от знания внешнего порядка вызовов. `OnAnotherModuleLoaded` является глобальным broadcast: каждый listener вызывается для каждого модуля, что создаёт неявные связи и квадратичный startup traversal.

Дополнительный smell: в `OnRegisterServices` передаются `IServiceRegister` и `IServiceContainer`, но фактически это один объект.

Предпочтительная модель:

```text
Describe dependencies -> construct services -> construct module -> initialize -> run -> dispose
```

Зависимости между модулями должны быть декларативными и проверяться до инициализации.

Статус: [ ] Не разобрано

### 6. Порядок систем задаётся хрупким протоколом

`IBuilder` принимает строковый `layer` и числовой `order`:

```csharp
IBuilder Add(object system, string layer = "BASIC_LAYER", int order = 0);
```

Проблемы:

- строковые идентификаторы допускают опечатки;
- refactoring tools не видят связи;
- одинаковые `order` требуют знания дополнительных правил сортировки;
- порядок одновременно определяется layers, order и scheduler metadata;
- фактический execution graph трудно увидеть из места регистрации системы.

Предпочтительное направление: typed phase/layer IDs, явные `Before<T>()` / `After<T>()` и автоматически построенный DAG на основе зависимостей и ECS access metadata.

Статус: [ ] Не разобрано

### 7. Гибрид source generation и reflection усложняет модель

Scheduler использует generated registry providers, но runtime всё равно:

- сканирует assemblies;
- ищет provider types;
- создаёт их через `Activator.CreateInstance`;
- использует reflection в `AddRunner<T>()`.

Это не обязательно является hot-path проблемой, поскольку pipeline строится редко. Основная проблема — неоднородная модель ошибок: часть ловится генератором, часть при startup, часть только при запуске системы.

Нужно выбрать более последовательное направление:

- либо полностью generated composition;
- либо простой reflection bootstrap с кэшем и строгой startup-валидацией.

Статус: [ ] Не разобрано

### 8. Диагностика ядра не оформлена как система

Core, runner и модули широко используют `Console.WriteLine`. При этом ошибки обрабатываются по-разному:

- warning в консоль;
- возврат `null`;
- generic `Exception`;
- проглатывание исключения после печати;
- продолжение работы с частично инициализированным состоянием.

Последствия:

- embedded и headless hosts не контролируют вывод;
- нет категорий и structured fields;
- диагностику сложно тестировать;
- отсутствует единая fail-fast policy для startup;
- важное сообщение может потеряться среди debug-вывода.

Желаемый результат: небольшой `IEngineDiagnostics` или event sink. Hot-path API должен быть allocation-free; форматирование можно переносить на потребителя.

Статус: [ ] Не разобрано

### 9. Структура проектов чрезмерно раздроблена

На момент исследования в репозитории находится 87 `.csproj`, из них 63 невендорных и 27 под `Modules`.

Разделение Core и backend-проектов полезно, но чрезмерная гранулярность создаёт налог:

- усложняется граф зависимостей;
- растёт стоимость restore и design-time build;
- добавляется project boilerplate;
- маленькие изменения затрагивают много project references;
- assembly boundaries начинают подменять осознанный API design.

Не следует сливать всё в монолит. Отдельный `.csproj` оправдан, если он обеспечивает независимую поставку, платформенную границу, отдельный backend или строгую границу зависимостей.

Статус: [ ] Не разобрано

### 10. Базовый tick rate зашит глобальными константами

`Application` глобально задаёт 50 Hz через `TICKS_PER_SECOND` и `TICK_DT`.

Для конкретной игры это допустимо, но для движка создаёт ограничения:

- сервер и клиент не могут удобно использовать разные rates;
- тесты не получают управляемую clock configuration;
- replay должен неявно полагаться на compile-time значение;
- невозможно создать несколько runtime/world с разной частотой;
- изменение rate требует перекомпиляции.

Желаемый результат: immutable `SimulationClockConfig`, передаваемый runtime. Значения должны один раз копироваться в поля loop/ticker, поэтому hot path не требует дополнительных lookup или аллокаций.

Статус: [ ] Не разобрано

## Suggested Order

Если исправлять постепенно, рекомендуемый порядок такой:

1. [ ] Определить целевую модель DI и composition root.
2. [ ] Упростить lifecycle и определить единственную основную модель систем.
3. [ ] Разделить ответственности `Runner` по подтверждённым архитектурным границам.
4. [ ] Сделать module graph и порядок инициализации явными.
5. [ ] Типизировать ordering API систем.
6. [ ] Унифицировать generated/reflection composition.
7. [ ] Ввести единый diagnostics contract и startup error policy.
8. [ ] Пересмотреть project boundaries.
9. [ ] Вынести simulation clock в runtime configuration.

Первые три пункта связаны. Не следует локально переписывать DI, не определив, кто после этого создаёт системы, модули и динамические assets. Для этого изменения нужен отдельный ADR или ExecPlan.

## Outcome

Основное направление KarpikEngine остаётся здравым: Client / Server / Shared boundaries, data-oriented gameplay и DragonECS подходят для real-time движка. Главная сложность сосредоточена в интеграционном слое вокруг этих частей: DI, lifecycle, module orchestration и `Runner`.

Приоритетная цель — не добавить новые абстракции, а уменьшить количество скрытых протоколов и сделать создание, порядок исполнения и lifetime объектов явными.

## Links

- Related plan: `plans/PLANS.md`
- Related ADRs: `docs/02_ADR/`
- Related code: `Karpik.Engine.Core/Services/`
- Related code: `Karpik.Engine.Core/LifeCycle/`
- Related code: `Karpik.Engine.Core.Runner/Runner.cs`
- Related code: `DragonExtensions/LifeCycleBridge.cs`
- Related code: `Karpik.Engine.Core/IInstaller.cs`
