---
title: "Подготовка ModSet и Simulation перед сборкой pipeline"
date: "2026-10-22"
status: "accepted"
tags:
  - adr
  - architecture
---

# Подготовка ModSet и Simulation перед сборкой pipeline

## Context

Lua-моды раньше загружались через `ISystemAsyncInit`, после сборки ECS pipeline.
На этом этапе уже нельзя добавлять экземпляры систем. Каталог модов был Engine
singleton, поэтому повторное сканирование меняло общие определения.

## Decision

`ISystemRegistry` остаётся контрактом `IModule.Add`: `Add<T>` и `ConfigurePipeline`
доступны до создания Simulation scope. Обе перегрузки `AddInstance` перенесены в
`ISystemRuntimeRegistry`. Одна реализация `SystemRegistry` хранит все дескрипторы;
Runner предоставляет runtime-контракт только в Simulation scope. После
`ResolveAndAdd` добавление экземпляров запрещено. Это не API изменения работающего графа.

`IModSetPreparation.PrepareAsync(CancellationToken)` вызывается после создания
ModSet scope и до создания Simulation scope. `ISimulationPreparation` с такой же
сигнатурой вызывается после восстановления restart-state, но до создания систем
и pipeline. В каждой фазе подготовители выполняются последовательно в порядке
DI-регистрации; зависимым операциям следует использовать один подготовитель,
а не предполагать порядок между независимо зарегистрированными сервисами.
Все известные зависимости внедряются через конструктор. Dynamic использует
атрибуты сервисов, Static — существующие сгенерированные фабрики без нового поиска.

`ModsRegistry` — ModSet singleton: успешный Scan фиксирует отсортированный по ID
каталог и запрещает повторное сканирование. Сканирование всех доступных модов
сохранено; UI выбора и отдельный выбранный gameplay-набор этим не реализованы.
`LuaModSetPreparation` выполняет Scan. `LuaSimulationPreparation` создаёт runtime
через Simulation-owned `ModsLifecycleManager`; старый Lua InitSystem удалён.

Подготовка ожидается через `SetupAsync`; токен отмены передаётся подготовителям
и обычным async-инициализаторам. При ошибке/отмене Runner освобождает дочерние
scopes раньше родительских; частично загруженные моды освобождаются в обратном
порядке. Отмена кооперативная, исполняющийся Lua-callback принудительно не прерывается.

Создание Simulation scope, вызовы подготовителей, конструирование систем и
pipeline.Init выполняются на главном потоке через существующий scheduler.
Host прокачивает очередь методом `MainThreadScheduler.RunUntilCompleted(Startup)`:
блокирующее ожидание Startup без прокачки может заблокировать возврат на main thread.
Sync Setup по-прежнему требует синхронного завершения: при незавершённой подготовке
он запрашивает отмену, наблюдает её окончание и лишь затем очищает scopes. До
окончания очистки повторный Setup запрещён. Если подготовитель игнорирует отмену,
нужно дождаться его завершения; преждевременное освобождение его зависимостей недопустимо.

## Consequences

Меняется API модулей: вызовы `ISystemRegistry.AddInstance` переносятся в
Simulation-подготовитель с `ISystemRuntimeRegistry` в конструкторе. Существующий
типовой путь регистрации сохраняется. Сканирование не повторяется для каждой
Simulation одного ModSet; VM и gameplay-состояние между Simulation не разделяются.
Текущий Runner создаёт одну Simulation на Setup: новые API создания нескольких
Simulation, hot reload и индивидуальные Lua-адаптеры остаются отдельными задачами.

Изменения выполняются только при запуске, не добавляют работу или аллокации в кадр,
не меняют fixed dt, компоненты ECS или Client/Server/Shared boundaries.

## Verification

`PreparationTests` проверяют Dynamic/Static, оба scheduler mode, порядок фаз,
constructor injection, позднюю регистрацию, async ожидание, ошибку, отмену и retry.
`LuaPreparationTests` проверяют владение каталогом, две независимые загрузки VM,
запрет повторного Scan и очистку частичной загрузки. Команды и результаты:
[план выполнения](../../plans/lua-system-registration-remaining.md).
