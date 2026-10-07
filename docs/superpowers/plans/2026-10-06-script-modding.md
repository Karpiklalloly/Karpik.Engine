# Script Modding: подготовка реализации

> **For agentic workers:** используйте superpowers:executing-plans для исполнения
> шагов. Не запускайте субагентов без отдельного основания или разрешения.

**Goal:** проверить интеграционные границы до реализации согласованного моддинга.

**Architecture:** существующие раннеры исполняют экземпляры фазовых адаптеров;
Simulation владеет state, immutable ModSet — определениями. Lua первый backend,
hot reload меняет версию транзакционно на границе тика.

**Tech Stack:** C#, .NET 10, DragonECS, существующий MoonSharp 2.0.0.

**Spec:** [согласованный дизайн](../../../plans/script-modding-design.md).
**Overview:** [живой ExecPlan](../../../plans/script-modding-execplan.md).

Статус: запланировано, runtime не изменён. Далее T1, затем T2. Блокеров для
исследования нет; cancellation, hard memory limits и DI switching не доказаны.
Этот план покрывает подготовку; runtime API фиксируются после её результатов.

## Global Constraints

- Нет обязательного бюджета времени или инструкций, ограничение только opt-in.
- Память/команды имеют отдельные ограничения, hot-path bridge без аллокаций.
- Все модовые компоненты публичны, системы первоначально последовательны.
- Сторона определяется Shared/Client/Server, gameplay fixed dt.
- Hot reload включён в первый объём, WASM и variable collections отложены.
- Не менять gameplay runtime в рамках подготовки, не добавлять новую VM dependency.

## Review Focus

- Повторный Type адаптера: Dragon acceptance не доказывает runner registry acceptance.
- VM cancellation: задержка измеренная после возврата не прерывает бесконечный цикл.
- Memory quota: лимит команд не доказывает лимит самой Lua VM.
- Reload scopes: старый parent scope нельзя освободить под живой Simulation.
- AOT: успешный publish без выполнения пользовательских bindings недостаточен.

## T1: Lua boundaries

**Files:** читать `Directory.Packages.props`,
`Modules/Shared/Modding/Modding.Lua/ModContainer.cs`, `ModManager.cs`,
`GameAPI.cs` и `docs/02_ADR/static-runtime-composition.md`.
Обновить `plans/script-modding-execplan.md` и этот документ результатами.

**Consumes:** требования бюджета и reload из Spec.
**Produces:** доказательства/ограничения callbacks, isolation, memory, interruption,
bindings/AOT; это данные исследования, новый public API пока не создаётся.

- [ ] Проследить подготовку, вызов, dispose и reload текущих Lua окружений;
  записать точные file/member pointers, в том числе shared mutable services.
- [ ] Проверить cancellation/quota механизм по исходникам установленной версии
  или первичной документации. Если нужен execution probe, сначала оформить
  изолированный worker с внешним watchdog, не запускать бесконечный callback
  в test runner или активной Simulation.
- [ ] Отдельно проверить механизм memory ceiling VM и область GC allocations;
  отсутствие механизма явно отметить, не заменять его post-call измерением.
- [ ] Проверить AOT binding surface по текущему ADR и calls; составить minimal
  publish/execution сценарий, не считать существующий warning inventory доказательством.
- [ ] Выполнить Lua build из Concrete Steps overview, записать фактический результат.
- [ ] Обновить оба плана; unsupported optional cancellation не блокирует безлимитный
  запуск, но требует явного пути остановки worker.

## T2: instance registration and reload integration

**Files:** читать `Karpik.Engine.Core/Services/ISystemRegistry.cs`,
`Karpik.Engine.Core.Runner/SystemRegistry.cs`, `Builder.cs`, `Runner.cs`,
`Karpik.Engine.Core/Scheduling/EcsUpdateGraphBuilder.cs`,
`third-parties/DragonECS/src/EcsPipeline.Builder.cs` и связанные runner tests.
Обновить оба плана результатами.

**Consumes:** Spec adapters/order/lifetime; результаты T1.
**Produces:** перечень минимальных изменений instance identity и DI lifetime,
точные signatures и отдельные задачи следующего implementation plan.

- [ ] Проследить все lifecycle registration paths и места использования Type;
  перечислить изменения для двух экземпляров одного типа без нового scheduler.
- [ ] Проверить требование «мод A → штатная система → мод B» для Update,
  RenderPrepare и layer/order фаз; описать failures duplicate type/order targets.
- [ ] Проверить Simulation/ModSet ownership и существующий reload flow;
  описать способ удержания старых scope ресурсов до commit, не объявлять
  его реализованным. Выявить внешние эффекты lifecycle hooks.
- [ ] Выполнить два filtered test commands из overview; записать baseline output.
- [ ] Зафиксировать proposed exact API и acceptance cases для adapters и graph;
  mixed component queries/storage/reload получают свои связанные задачи.
- [ ] Сверить покрытие всех разделов Spec, обновить оба документа и согласовать
  concrete implementation plan перед кодом. Каждый независимый runtime task
  получает собственную task branch от integration branch и PR.

## Verification record

Baseline build/tests, execution probes, AOT и allocation измерения ещё не запускались.
Запись документов завершена; T1/T2 и реализация остаются незавершёнными.
