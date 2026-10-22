# Что осталось для регистрации Lua-систем

Состояние на 2026-10-22, ветка `codex/modding-0.7`.
Это запись для продолжения работы; обзор — [ExecPlan](script-modding-execplan.md).
Этапы подготовки ModSet/Simulation и разделение реестров реализованы;
объявления Lua-систем и отдельные адаптеры ниже ещё не реализованы.

## Уже сделано

- [x] Регистрация экземпляров через `ISystemRegistry.AddInstance(...)`.
- [x] Стабильные ID систем, зависимости по ID и несколько экземпляров одного
  CLR-типа в Update-графе и планировщике.
- [x] Перегрузка `AddInstance(ISystem, EcsUpdateSystemDescriptor, ...)`:
  собственные reads/writes, порядок и `IsSequential` для каждого экземпляра.
  Проверяются ID, совпадение типа и наличие `ISystemUpdate`.
- [x] `SystemRegistry.TryGetUpdateDescriptor(...)` и приоритет метаданных
  экземпляра в `EngineRunner.BindUpdateDescriptors(...)`.
  Старая регистрация использует сгенерированные метаданные типа.
- [x] Проверки Dynamic/Static и Deterministic/Parallel. Последний профильный
  прогон движка: 42/42 теста.
- [x] Исправлены воспроизведённые зависания и потеря элементов очереди Jobs.
  После исправления: 62/62 теста подмодуля, по пять прогонов Debug и Release;
  проверки нулевых managed-аллокаций проходят. Это не гарантия всех возможных
  межпоточных сценариев или предельной задержки под игровой нагрузкой.

Коммиты: `8edd9a4` — инфраструктура runtime-систем;
`9383e72` в Karpik.Jobs — исправления планировщика;
`d44edd1` в движке — обновление подмодуля, регрессия и уточнение ADR;
`7cc0360` — метаданные экземпляров.
Коммиты локальные; публикация в рамках этих шагов не выполнялась.

## 1. Подготовить моды до закрытия регистрации систем

### Согласованное уточнение и выполнение, 2026-10-22

Разделяем `ISystemRegistry` (типы и конфигурация pipeline) и
`ISystemRuntimeRegistry` (обе перегрузки `AddInstance`). Реализация одна;
runtime-контракт доступен только в Simulation scope и закрывается при сборке pipeline.
`IModSetPreparation.PrepareAsync(CancellationToken)` выполняется после создания
ModSet scope, до Simulation scope. `ISimulationPreparation` с той же сигнатурой
выполняется до `ResolveAndAdd`; зависимости передаются через конструктор.
`ModsRegistry` принадлежит ModSet и фиксирует каталог один раз, VM и
`ModsLifecycleManager` остаются в Simulation. Выбор включённых модов и Lua-адаптеры
не входят в этот шаг. Выполняем в текущей `codex/modding-0.7`, без новой ветки от неё.

- [x] P1: Регрессионные тесты порядка, DI, поздней регистрации, async/ошибок/отмены.
- [x] P2: Разделение контрактов, этапы Runner и перенос существующих тестовых модулей.
- [x] P3: Lua-подготовка, ModSet-каталог, отмена и освобождение частичной загрузки.
- [x] P4: Пересборка, Dynamic/Static и scheduler-тесты; итоговое review.

Review: два P1 исправлены в этом шаге. Реальные async-подготовители не должны
переносить создание scope/систем и pipeline.Init на фон; используем существующий
MainThreadScheduler.InvokeAsync. Host-ы CoreRunner и WorkerHost прокачивают Startup
через RunUntilCompleted. Sync Setup отменяет неподдерживаемую async-подготовку,
наблюдает её окончание и сохраняет setup-pending до очистки: noncooperative callback
не освобождает зависимости преждевременно и не может повредить повторный запуск.
Регрессии RED: два неверных thread ID и преждевременный ModSet.Dispose; GREEN:
41/41 профильных тестов после исправления, затем 43/43 с Bootstrap boundary.
Дополнительно прошёл реальный файловый Lua-мод (1/1), не только fake loader.

Итог P4: Rebuild Runner.Tests с `-t:Rebuild --no-restore -m:1 -nr:false` —
0 ошибок, 44 предупреждения. Полный `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj --no-build --no-restore -m:1 -nr:false --blame-hang --blame-hang-timeout 30s --blame-hang-dump-type none`
— 189/189, без пропусков. `git diff --ignore-submodules --check` без ошибок.
Ручная игра, NativeAOT publish и индивидуальные Lua-адаптеры не проверялись/не выполнялись.

Публикация не выполнялась: ветка `codex/modding-0.7` уже используется открытым
PR #101 `fix: Restore cooked asset loading during modding refactor` против main.
Чтобы не расширять отдельный PR другой задачей, нужно согласовать выделение
зависимой ветки либо порядок интеграции; правило AGENTS.md запрещает самостоятельно
ответвляться от task-ветки. По запросу разработчика изменения подготовлены для локального
коммита в текущей task-ветке; исходные правки планов сохранены. Push этим не разрешён.
Подготовленный PR title: `feat!: Prepare ModSet and Simulation before building the ECS pipeline`.
Описание: runtime-системы раньше нельзя было добавлять после позднего Lua InitAsync.
Разделены типовой и runtime-реестры, подготовка выполняется до pipeline; каталог
изолирован в ModSet, VM в Simulation. Host ожидает startup с прокачкой main-thread
scheduler. Проверки: Rebuild, 189/189 Runner tests, независимое review с устранением
двух P1. Совместимость: `AddInstance` перенесён в `ISystemRuntimeRegistry`; модули
с регистрацией экземпляров требуют переноса вызовов в Simulation preparation.

Проверка из корня: `dotnet build tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false`,
затем `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj --no-build --no-restore -m:1 -nr:false --filter "FullyQualifiedName~Preparation|FullyQualifiedName~RegisteredInstanceRunnerTests|FullyQualifiedName~AutofacCompositionTests|FullyQualifiedName~StaticCompositionTests|FullyQualifiedName~EcsUpdateSchedulerRuntimeTests|FullyQualifiedName~EcsRenderPrepareSchedulerRuntimeTests|FullyQualifiedName~EngineRunnerLifecycleTests" --blame-hang --blame-hang-timeout 30s --blame-hang-dump-type none`.
Ожидается порядок ModSet preparation -> Simulation preparation -> Init -> AsyncInit,
однократная подготовка, отказ при поздней регистрации и очистка при ошибке/отмене.
Изменения затрагивают только startup; кадр, ECS layout, fixed dt и границы сторон
не меняются. Удаление `AddInstance` из старого интерфейса является изменением API.
Откат: вернуть контракты, Runner и Lua-подготовку вместе; не оставлять два пути загрузки.
При ошибке подготовки Runner освобождает scopes в порядке Simulation -> ModSet -> Engine.
Проверка P1–P3: первый тест запрета `AddInstance` на старом интерфейсе воспроизвёл
`Assert.DoesNotContain` (RED), затем профильный прогон дал 69/69 (GREEN).
Отмена кооперативная: между загрузками и после возврата из загрузчика/callback,
без обещания прерывания исполняющегося Lua. При ошибке проверен повторный SetupAsync
без пересоздания Runner; полный Destroy является терминальным и очищает его регистрации.
Runtime-реестр пока используется тестовыми подготовителями; Lua-подготовителю он
понадобится в шаге 3, добавлять неиспользуемую зависимость сейчас не требуется.

- [x] Согласовать и добавить этап подготовки Simulation после создания её
  сервисов, но до `SystemRegistry.ResolveAndAdd(...)` и сборки pipeline.
- [x] Контракты: `IModSetPreparation.PrepareAsync(CancellationToken)` и
  `ISimulationPreparation.PrepareAsync(CancellationToken)`.
- [x] Реализация: `LuaSimulationPreparation` с конструкторными
  зависимостями `IModsRegistry`, `IModsLifecycleManager` и `Application`.
  Подключать через существующую DI и поддержать Dynamic и Static без нового
  reflection-поиска в Static и без service locator для известных зависимостей.
- [x] Вынести сканирование и загрузку модов из `InitSystem.InitAsync()` в эти
  этапы. Исключить повторную загрузку; остальные async-инициализаторы автоматически
  не переносить.
- [x] Проверить отмену, ошибку загрузки и освобождение частично подготовленных
  runtime в обратном порядке. Подготовка должна завершаться до первого кадра.

Причина изменения: ранее `Runner.cs` собирал pipeline до загрузки модов через
`InitAsync`. Теперь подготовители вызываются раньше, а обычные async-инициализаторы
сохраняют своё место после инициализации pipeline.

`InitSystem` удалён. Классы `BeginSystem`, `UpdateSystem` и `FixedUpdateSystem`
по-прежнему находятся вместе в
`Modules/Shared/Modding/Modding.Lua/Systems/UpdateSystem.cs`.

## 2. Подготовить описания отдельных Lua-систем

- [ ] Согласовать минимальный формат объявления системы: локальный ID,
  callback, фаза Update, reads/writes и зависимости before/after.
- [ ] Предлагаемые типы: `LuaSystemDeclaration` и
  `LuaSystemDeclarations.Read(...)` внутри `Modding.Lua`.
- [ ] Прочитать объявления из таблиц, возвращённых entry point-ами,
  и заранее связать Lua-функции и метаданные компонентов.
- [ ] Сформировать уникальные ID в пределах Simulation, например
  `<modId>:<systemId>`. Проверить дубли, отсутствующие функции, неверные типы
  полей, неизвестные компоненты и цели порядка. Ошибки сообщать до первого кадра
  с ID мода и системы.

В этом шаге использовать известные CLR-компоненты. Динамические схемы
компонентов и полноценный доступ к ECS-данным — отдельная работа.

## 3. Добавить и зарегистрировать Lua-адаптер

- [ ] Добавить `LuaUpdateSystem : ISystemUpdate` с методом `Update()`:
  один экземпляр вызывает одну заранее связанную Lua-функцию.
- [ ] Добавить внутренний путь вызова в `LuaScriptRuntime`, например
  `InvokeSystem(...)`, сохранив проверки disposal и повторного входа.
  Не создавать новую VM на каждую систему; runtime принадлежит Simulation.
- [ ] Зарегистрировать адаптеры на этапе подготовки через новую перегрузку
  `ISystemRuntimeRegistry.AddInstance(system, descriptor, ...)`.
- [ ] Для первого варианта задавать `IsSequential = true`: функции одной
  MoonSharp VM нельзя исполнять одновременно. Выбирать порядок по ID и графу.
- [ ] Явно определить совместимость с существующим `on_update` и групповым
  `UpdateSystem`: не допустить двойного вызова одной и той же игровой функции.
- [ ] Сохранить однократные start/destroy/unload и освобождение общего runtime.

MoonSharp остаётся выбранной VM. C#-мост не должен добавлять выделения памяти
в Update; существующие аллокации самой MoonSharp измерять отдельно и не
объявлять весь Lua-вызов zero-GC. Физика и игровая логика с фиксированным шагом
должны использовать fixed dt; индивидуальная FixedUpdate-интеграция пока не готова.

## 4. Проверить сквозной сценарий

- [ ] Реальный Lua-мод объявляет две системы одного CLR-типа с разными ID
  и порядком; обе выполняются ровно один раз за Update в ожидаемом порядке.
- [ ] Проверить порядок Lua-систем относительно штатной C#-системы,
  Dynamic/Static и Deterministic/Parallel.
- [ ] Проверить ранний отказ при неверном объявлении, недопустимой зависимости
  и ошибке подготовки; повторное создание и уничтожение Simulation.
- [ ] Отдельно измерить аллокации моста и VM. Провести ручной запуск игры;
  автоматический тест не заменяет эту проверку.

Рабочий каталог команд — корень KarpikEngine. Сборки выполнять с `-m:1 -nr:false`.
Базовый профильный прогон после пересборки изменённых проектов и тестового проекта:

```powershell
dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj --no-build --no-restore -m:1 -nr:false --filter "FullyQualifiedName~RegisteredInstanceRunnerTests|FullyQualifiedName~AutofacCompositionTests|FullyQualifiedName~StaticCompositionTests|FullyQualifiedName~EcsUpdateSchedulerRuntimeTests|FullyQualifiedName~EcsRenderPrepareSchedulerRuntimeTests" --blame-hang --blame-hang-timeout 30s --blame-hang-dump-type none
```

Добавить отдельные тесты Lua-интеграции в соответствующий тестовый проект;
приведённый фильтр сам по себе её не проверяет. Для изменённых Lua-callback-ов
использовать `dotnet build <project> -t:Rebuild -m:1 -nr:false`.

## Позже, вне ближайшего шага

Индивидуальные FixedUpdate/RenderPrepare-системы; ECS queries и пакетный доступ
к данным; динамические компоненты; буфер структурных команд; перенос состояния
при hot reload, миграции и репликация. Требования и более широкий согласованный
дизайн находятся в `plans/script-modding-requirements.md` и
`plans/script-modding-design.md`; эта запись не меняет их и не означает,
что перечисленные возможности уже поддерживаются.
