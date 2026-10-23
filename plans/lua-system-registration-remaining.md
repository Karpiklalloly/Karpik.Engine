# Что осталось для регистрации Lua-систем

Состояние на 2026-10-23, ветка `codex/modding-0.7`.
Это запись для продолжения работы; обзор — [ExecPlan](script-modding-execplan.md).
Этапы подготовки ModSet/Simulation и разделение реестров реализованы;
объявления Lua Update-систем и отдельные адаптеры реализованы и проверены локально.

## Реализация 2026-10-23

Согласованный контракт: entry point возвращает `systems`, плотный массив таблиц
с `id`, `phase = "update"`, Lua-функцией `callback` и необязательными массивами
`reads`, `writes`, `before`, `after`. ID адаптера — `<modId>:<id>`;
зависимости содержат полные ID. Даже пустой `systems` запрещает `on_update`
во всех entry points того же мода. Старые моды без `systems` работают без изменений.

- [x] L1: тесты реальных Lua-объявлений, неверных полей, конфликтов между entry points.
- [x] L2: разбор в `LuaSystemDeclarations.cs`, хранение функций в `LuaScriptRuntime`,
  внутренний `InvokeSystem(int, double)` с прежними защитами и очисткой ссылок.
- [x] L3: `LuaUpdateSystem`, регистрация из `LuaSimulationPreparation`, общий
  lifecycle-gate в `ModContainer`, пропуск нового формата групповым Update.
- [x] L4: Dynamic/Static × Deterministic/Parallel: Lua A → native → Lua B,
  disabled/re-enable, disposal, ошибочные зависимости и повторная Simulation.
- [x] L5: Rebuild, Runner suite, сравнение allocations прямого вызова и адаптера,
  review, актуализация документов.

Решение реализации: имена CLR-компонентов задаёт явный `LuaComponentRegistry`
с `Register<T>(name)` для struct + IEcsComponent. Не вводить reflection-поиск
сборок в Static; неизвестное имя отклоняется до кадра. Реестр — метаданные Engine,
runtime/функции и адаптеры принадлежат Simulation. Нет новых ECS bindings.
Регистрация через существующий `ISystemRuntimeRegistry`; граф проверяет цели и циклы.
Парсер создаёт массивы при загрузке; адаптер не создаёт аргументы/делегаты в Update.
Аллокации MoonSharp остаются отдельно измеряемым ограничением.

Уточнение по review разработчика: production friend assembly удалён. Межмодульный
контракт ModContainer теперь явный: readonly `IScriptRuntime Runtime` (владение
и disposal остаются у контейнера) и публичный `TryStartUpdate()` с проверками
disposal, enabled и однократного Start. После удаления InternalsVisibleTo сборка
Lua воспроизвела три ошибки недоступного API; исправление — видимость только этих
двух членов, без новых интерфейсов, делегатов или изменений горячего пути.
Повторная проверка после удаления friend assembly: Rebuild — 0 ошибок,
46 предупреждений; весь Runner — 215/215; diff-check без ошибок.

Ruling 2026-10-23: `LuaUpdateSystem` — readonly struct, упакованный один раз
при AddInstance. Генератор считает все классы ECS-систем DI-кандидатами;
runtime-адаптеру нужны конкретные owner/VM/index, поэтому его нельзя создавать
из обычного typed factory. Struct исключён существующим правилом discovery,
без изменения генератора и без дополнительного wrapper/attribute. В кадре
используется уже упакованный ISystemUpdate; повторного boxing нет.
`SequentialSystemAttribute` расширен на Struct, чтобы анализатор K003 сохранял
проверку непрозрачного Lua-вызова. Проверка AttributeTargets прошла RED перед правкой.

Проверки из корня: `dotnet build tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -t:Rebuild --no-restore -m:1 -nr:false`,
затем `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj --no-build --no-restore -m:1 -nr:false --blame-hang --blame-hang-timeout 30s --blame-hang-dump-type none`.
Откат: убрать parser/adapters и их регистрацию вместе, сохранив прежний `on_update`.
Ручная игра и NativeAOT publish не входят в автоматический прогон.

Наблюдения 2026-10-23: 15 новых отрицательных проверок сначала упали из-за
игнорирования `systems`; после реализации Lua-набор 32/32. Rebuild успешен:
0 ошибок, 46 предупреждений; полный Runner-набор 215/215, ECS.Core 49/49.
Генератор: Client/Server parity и typed-factory проверки 4/4 после исправления
конфликта class-adapter с DI-discovery. Allocation-check сравнивает 1000 прямых MoonSharp
вызовов, вызовов через runtime и через адаптер: объёмы равны, отключённый адаптер
даёт 0 байт. Это не обещание zero-GC Lua. Независимое review, включая повторную
проверку struct/boxing/AttributeTargets, не нашло P1/P2. `git diff --check` чистый.
Static проверяет регистрацию адаптеров и native interleaving; штатный групповой
Update подключён в Dynamic-тесте. Повторная Simulation создаёт новый runtime.

Команды дополнительных проверок (cwd — корень):
`dotnet test tests/ECS.Core.Tests/ECS.Core.Tests.csproj --no-restore -m:1 -nr:false`;
`dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj --no-restore -m:1 -nr:false --filter "FullyQualifiedName~GeneratedStaticRegistration_MatchesAttributeDiscovery_ServiceAndSystemSets|FullyQualifiedName~Run_ClientStatic_ResolvesConstructorDependenciesInDeclarationOrderViaTypedResolve|FullyQualifiedName~GeneratedServiceFactories_ConstructInstancesWithResolvedDependenciesWhenExecuted"`.

Публикация: GitHub проверен 2026-10-23; PR #101 открыт, draft, head — эта ветка,
тема cooked assets. Push не выполнялся, чтобы не расширять чужой scope PR.
Отдельная зависимая ветка требует разрешения разработчика по AGENTS.md.
Подготовленный заголовок: `feat: Run declared Lua systems in the ECS update pipeline`.
Описание PR: мод объявляет отдельные Update-системы с функциями callback,
metadata доступа и ID-зависимостями; подготовка регистрирует sequential-адаптеры,
сохраняя enabled/start/destroy/unload и старый on_update. Ошибочные объявления
и смешивание форматов отклоняются до кадра. Проверено: Rebuild, Runner 215/215,
ECS 49/49, generator 4/4, allocation comparison, независимое review.
Совместимость: прямым callers LuaSimulationPreparation нужны runtime registry,
Time и LuaComponentRegistry; generated DI учитывает новые параметры. `systems`
становится зарезервированным полем entry point. Ручная игра и AOT не проверялись.

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

- [x] Согласовать минимальный формат объявления системы: локальный ID,
  callback, фаза Update, reads/writes и зависимости before/after.
- [x] Реализованные типы: `LuaSystemDeclaration` и
  `LuaSystemDeclarations.Read(...)` внутри `Modding.Lua`.
- [x] Прочитать объявления из таблиц, возвращённых entry point-ами,
  и заранее связать Lua-функции и метаданные компонентов.
- [x] Сформировать уникальные ID в пределах Simulation, например
  `<modId>:<systemId>`. Проверить дубли, отсутствующие функции, неверные типы
  полей, неизвестные компоненты и цели порядка. Ошибки сообщать до первого кадра
  с ID мода и системы.

В этом шаге использовать известные CLR-компоненты. Динамические схемы
компонентов и полноценный доступ к ECS-данным — отдельная работа.

## 3. Добавить и зарегистрировать Lua-адаптер

- [x] Добавить `LuaUpdateSystem : ISystemUpdate` с методом `Update()`:
  один экземпляр вызывает одну заранее связанную Lua-функцию.
- [x] Добавить внутренний путь вызова в `LuaScriptRuntime`, например
  `InvokeSystem(...)`, сохранив проверки disposal и повторного входа.
  Не создавать новую VM на каждую систему; runtime принадлежит Simulation.
- [x] Зарегистрировать адаптеры на этапе подготовки через новую перегрузку
  `ISystemRuntimeRegistry.AddInstance(system, descriptor, ...)`.
- [x] Для первого варианта задавать `IsSequential = true`: функции одной
  MoonSharp VM нельзя исполнять одновременно. Выбирать порядок по ID и графу.
- [x] Явно определить совместимость с существующим `on_update` и групповым
  `UpdateSystem`: не допустить двойного вызова одной и той же игровой функции.
- [x] Сохранить однократные start/destroy/unload и освобождение общего runtime.

MoonSharp остаётся выбранной VM. C#-мост не должен добавлять выделения памяти
в Update; существующие аллокации самой MoonSharp измерять отдельно и не
объявлять весь Lua-вызов zero-GC. Физика и игровая логика с фиксированным шагом
должны использовать fixed dt; индивидуальная FixedUpdate-интеграция пока не готова.

## 4. Проверить сквозной сценарий

- [x] Реальный Lua-мод объявляет две системы одного CLR-типа с разными ID
  и порядком; обе выполняются ровно один раз за Update в ожидаемом порядке.
- [x] Проверить порядок Lua-систем относительно штатной C#-системы,
  Dynamic/Static и Deterministic/Parallel.
- [x] Проверить ранний отказ при неверном объявлении, недопустимой зависимости
  и ошибке подготовки; повторное создание и уничтожение Simulation.
- [x] Отдельно измерить аллокации моста и VM.
- [ ] Провести ручной запуск игры; автоматический тест не заменяет эту проверку.

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
