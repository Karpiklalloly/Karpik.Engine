# Аудит KarpikEngine — 6–7 сентября 2026

Проверено текущее рабочее дерево, включая незакоммиченные изменения Content. Найдены **34 дефекта: 10 P1 и 24 P2**. P1 — риск потери данных, зависания, нарушения изоляции состояния или конкурентного исполнения; P2 — остальные ошибки корректности, сборки и явно нарушенный контракт real-time. Это приоритет исправления, а не оценка вероятности каждого сценария.

Производственный код и тесты не исправлялись. Итог охватывает основные подсистемы, но не означает построчного доказательства корректности всего репозитория. Ниже отдельно указаны исполнения минимальных сценариев, доказательства по исходникам и ограничения проверок.

## Что исправлять первым

1. Жизненный цикл задач Jobs: потеря завершённых зависимостей, конкурентная запись в deque, повторная публикация.
2. AssetsManager: проверка переименования до записи и единое владение параллельно загружаемым ресурсом.
3. StatPool: согласованность свободных слотов и рост sparse mapping.
4. Наследуемые ECS-аннотации, зависающий Tween.Simulate и потеря отпускания клавиш.
5. Затем версии Content, IPC framing и работоспособность Content в установленном SDK.

## Jobs и native memory

**J1 · P1 · Завершённая зависимость снова становится незавершённой после переиспользования слота.** `first-parties/Karpik.Jobs/Karpik.Jobs/JobScheduler.cs:610–612`, проверка завершения `356–361`, пересчёт зависимостей `599–603`. При capacity=2: создать A и B(depends A), завершить A, создать и завершить C в освободившемся слоте A. Записанное поколение завершения заменяется, B больше не может выполниться. Минимальный запуск дал `A completed=False, B runnable=False`. Нужно сохранять факт завершения до потребления зависимыми задачами.

**J2 · P1 · Несколько producer пишут в single-producer deque.** `first-parties/Karpik.Jobs/Karpik.Jobs/JobScheduler.cs:663–674`, `WorkStealingDeque.cs:48–64`. Worker возвращает ещё не готовую украденную задачу в исходную очередь, куда одновременно пишут другие workers и обычная публикация. Две записи могут занять один bottom и потерять задачу. Кроме того, неуспешный requeue только увеличивает счётчик, оставляя задачу без очереди. Подтверждено последовательностью операций в исходниках; детерминированный многопоточный stress-test не выполнялся. Исправление должно одновременно обеспечить правильное число producer и сохранность работы при переполнении.

**J3 · P1 · Повторная публикация выполняет одну задачу дважды.** `first-parties/Karpik.Jobs/Karpik.Jobs/JobScheduler.cs:405–426`. Один handle принимается очередями 0 и 1 без атомарного перехода rented → published/executing. Проверка дала `duplicate accepted=True/True`, `duplicate execution count=2`. Первый worker может вернуть descriptor в pool, пока второй ещё работает. Нужен атомарный захват состояния исполнения/публикации.

**J4 · P2 · Принятое выравнивание не гарантируется возвращённым адресом.** `first-parties/Karpik.Jobs/Karpik.Memory/NativeLinearAllocator.cs:44`, `NativeArena.cs:33–38,85–89`. Выравнивается offset относительно базы с более слабым alignment. При owner alignment=16 и запросе 65536 оба аллокатора вернули адрес с остатком 3120. Следует проверять ограничения базы либо выравнивать абсолютный адрес с учётом оставшейся ёмкости; Arena может выделять подходящий новый блок.

**J5 · P2 · Handle другого scheduler принимается за локальную задачу.** `first-parties/Karpik.Jobs/Karpik.Jobs/JobDescriptorHandle.cs:3–14`, `JobDescriptorPool.cs:144–154`. Handle содержит только index/generation. У двух новых scheduler capacity=1 значения совпадают: `B.Complete(handleFromA)` выполнил задачу B. Подтверждено запуском. Проверке нужна идентичность владельца, включая зависимости.

## Runtime, ECS codegen и IPC

**R1 · P1 · Генераторы теряют наследуемые ограничения scheduler.** `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/EcsUpdateRegistryGenerator.cs:66`, `RuntimeCompositionGenerator.cs:1269`. `GetAttributes()` читает только атрибуты текущего типа, хотя SequentialSystem/EcsAccess/EcsOrder объявлены наследуемыми. Derived от `[SequentialSystem] Base` получает `IsSequential: false`; аналогично теряются Reads/Writes и ограничения порядка. Registry → discovery → scheduler доверяет этим данным и допускает worker/concurrent execution. Roslyn-fixture на текущем исходнике подтвердил неверный descriptor; статический генератор проверен по исходнику. Нужно учитывать BaseType и семантику AttributeUsage в обоих путях.

**R2 · P2 · Само объявление generic/private системы ломает сгенерированный registry.** `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/EcsUpdateRegistryGenerator.cs:30–38,151`. Фильтр пропускает любой concrete class, затем генерирует `typeof(Generic<T>)` вне generic-контекста или ссылку на private nested type. Текущий Roslyn-fixture получил CS0246 и CS0122 соответственно, даже без регистрации этих типов пользователем. Генерировать нужно доступные материализуемые типы/закрытые регистрации.

**R3 · P2 · Оборванный IPC frame всё равно передаётся обработчику.** `Karpik.Engine.Core/ProcessManagement/IpcServer.cs:216–247`, `IpcClient.cs:104–138`. EOF прерывает внутренний цикл чтения, после чего dispatch получает неполный header или дополненный нулями body. `IpcProtocol.cs:69–74` также не ограничивает signed длину; `HotReloadState` допускает короткий ReadBytes. По исходникам подтверждён неправильный переход при EOF. Отдельный pipe-repro остановился на sandbox UnauthorizedAccess до передачи frame; успешное runtime-воспроизведение именно EOF не заявляется. Нужны exact reads, предел длины и отказ от dispatch неполных сообщений.

## Assets, Stats, Input, Modding и Tween

**M1 · P1 · Save повреждает чужой asset до отказа в переименовании.** `Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs:188–206`. Загрузить A и B одного типа, сохранить A по пути B: OpenWrite/saver выполняются до проверки коллизии нового ключа. Ошибка возникает после записи в B. Подтверждено порядком кода; конкретный объём повреждения зависит от saver. Проверять и резервировать destination до открытия файла.

**M2 · P1 · Параллельные загрузки нарушают владение cache entry.** `Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs:113–160,261–266`. Две загрузки проходят начальный lookup, проигравший TryAdd всё равно возвращается вызывающему. Его освобождение удаляет ключ победителя без сравнения instance, хотя действующие handles ещё существуют. Оба публичных пути загрузки ведут сюда. Подтверждено по коду; нужны объединение загрузок одного ключа и освобождение именно своей записи.

**M3 · P1 · Разреженный entity ID выходит за однократное расширение StatPool.** `Modules/Shared/StatAndAbilities/Core/StatPool.cs:165–169`, обращение `28–29`. Mapping длины 4 при Add(8) увеличивается до 8, затем индекс 8 вызывает IndexOutOfRangeException. Другие методы используют тот же helper. Проверено по коду. Рост должен обеспечивать как минимум entityID+1.

**M4 · P1 · ClearAll позволяет двум entities разделить один stat slot.** `Modules/Shared/StatAndAbilities/Core/StatPool.cs:139–145`. Add(0/1/2) занимает slots 1/2/3; Del(0) возвращает 1. ClearAll теряет старый free-list и сохраняет только 2/3. Следующие Add(0/1/2) берут 3/2/3: entities 0 и 2 ссылаются на один stat. Подтверждено ручной трассировкой всех счётчиков. Сбросить согласованно high-water/count/free-list.

**M5 · P1 · Захват клавиатуры теряет отпускание игровой клавиши.** `Modules/Client/Input/Input.cs:172–176`. W down → keyboard capture → W up → release capture оставляет игровое IsDown=true: release подавлен, обычной reconciliation нет. SDL сообщает release только в текущем pump. Подтверждено цепочкой публикации по исходникам. Физическое состояние и отпускания должны обновляться и во время capture.

**M6 · P2 · Восстановление после переполнения input требует нового события.** `Modules/Client/Input/Input.cs:545–573`. Заполнить ring, потерять последний release и прекратить ввод: `_overflowPending` обслуживается только следующим TryEnqueue, consumer не узнает о необходимости resync. Подтверждено по коду; существующий тест добавляет следующее событие и не проверяет тишину. Передавать overflow независимо от следующего ввода.

**M7 · P2 · Mouse delta теряется или применяется повторно при разных частотах.** `Modules/Client/Input/Input.cs:267–269,433–438`. Два consume после одного publish повторяют delta; два publish перед consume перезаписывают первую. Platform/simulation имеют отдельные lifecycle-системы. Подтверждено по коду, timing-test не выполнялся. Накапливать до consume и согласованно сбрасывать обе оси.

**M8 · P2 · Включение изначально выключенного мода пропускает Start.** `Modules/Shared/Modding/Modding.Lua/ModContainer.cs:22–27,105–109`. Setter вызывает Start до изменения IsEnabled, поэтому Start сразу возвращается. Подтверждено по коду. Изменить состояние до вызова startup, сохранив once-only семантику.

**M9 · P2 · Lua callbacks предыдущего root script регистрируются повторно.** `Modules/Shared/Modding/Modding.Lua/ModContainer.cs:165–192`. A.lua объявляет OnUpdate, B.lua — нет. Общий Script.Globals сохраняет A.OnUpdate, и загрузка B регистрирует его ещё раз. Подтверждено по коду. Изолировать lifecycle bindings скриптов либо очищать их перед выполнением следующего root script.

**M10 · P2 · Изменения эффектов оставляют stat cache устаревшим.** `StatAndAbilities.Codegen/StatAndAbilities.Codegen/StatGenerator.cs:104–107,122–154`. После чтения ModifiedValue ранняя вставка эффекта возвращается до IsDirty; RemoveEffect/ClearEffects также не ставят dirty. Следующее чтение возвращает старое значение. Подтверждено по исходнику генерации, отдельная compilation-fixture не запускалась. Инвалидировать cache при каждой успешной мутации.

**M11 · P2 · Одноимённые stats в разных namespaces конфликтуют в codegen.** `StatAndAbilities.Codegen/StatAndAbilities.Codegen/StatGenerator.cs:82`, `GenStatStruct.cs:48–50,105–112`. A.Health и B.Health передают AddSource одинаковое имя Health.Stat.g.cs. Подтверждено по коду генератора. Использовать полную идентичность типа. Global namespace также требует отдельной проверки, но не считается дополнительной находкой.

**M12 · P2 · SDL input выделяет память в каждом platform frame.** `Modules/Client/Window/Window.Sdl2/SDL2InputSource.cs:39–44`. Четыре LINQ→List и два immutable-set преобразования выполняются в Update; списки создаются даже при пустом вводе. Это прямое нарушение принятого no-allocation контракта. Наличие allocation видно по коду, bytes/frame и GC latency не измерялись. Переиспользовать ограниченные буферы/bitsets.

**T1 · P1 · Simulate зависает у paused/zero-speed tween.** `Modules/Shared/Tween/Tween.Core/Tween/Tweens/GTween.cs:208`. Внутренний цикл ожидает прогресс, который не меняется при pause или TimeScale=0. Изолированное воспроизведение не завершилось за контрольные 500 ms. Обработать отсутствие прогресса до цикла.

**T2 · P2 · Simulate теряет целое число duration.** `Modules/Shared/Tween/Tween.Core/Tween/Tweens/GTween.cs:204`. `% duration` превращает Simulate(1) при duration=1 в ноль; воспроизведён нулевой прогресс. Сохранить число циклов и корректно обработать точную границу.

**T3 · P2 · Context.Clear оставляет tween формально живым.** `Modules/Shared/Tween/Tween.Core/Tween/Contexts/GTweensContext.cs:98–102`. Коллекции очищаются без сброса IsAlive, повторный Play не возвращает tween в исполнение. Минимальный запуск оставил progress=0.25. Согласовать lifecycle объектов с очисткой context.

**T4 · P2 · Повторный запуск WaitTimeTween остаётся завершённым.** `Modules/Shared/Tween/Tween.Core/Tween/TweenBehaviours/WaitTimeTween.cs:16`. Start сбрасывает elapsed, но не finished. Воспроизведено преждевременное завершение повторного запуска после 0.1 tick. Сбрасывать всё состояние текущего запуска.

## Content

**C1 · P2 · Raw JSON молча превращается в пустой объект.** `Karpik.Content.Runtime/ContentRegistry.cs:184`. RawJsonProcessor сохраняет исходный JSON, а runtime десериализует его как wrapper RawJsonPayload с полем Json. Загрузка `{"x":42}` дала `Json == "{}"`. Проверено изолированным запуском. Для raw формата нужно оборачивать исходный текст, а не десериализовать его как wrapper.

**C2 · P2 · Загрузка неверной версии инвалидирует действующие ссылки.** `Karpik.Content.Runtime/ContentRegistry.cs:100–106`. После загрузки version=1 запрос ref version=999 запускает reload, повышает slot version до 2 и успешно завершает Task. Версия 999 по-прежнему недоступна, корректная ссылка version=1 уже недействительна. Воспроизведено. Разделить проверку версии и явную операцию reload; неподходящая ссылка не должна мутировать загруженный slot.

**C3 · P2 · После замены manifest старый lease снова считается живым.** `Karpik.Content.Runtime/ContentRegistry.cs:16–23`. RegisterManifest сбрасывает поколения новых slots, первая загрузка вновь получает version=1. Старый lease совпадает по id/version, IsAlive=true, но содержит прежний payload. Проверка показала `old lease alive ... True, payload=old`. Нужен epoch регистрации или проверка идентичности slot.

**C4 · P2 · Зависимости processor обходят проверку графа.** `Karpik.Content.Core/ContentBuildCoordinator.cs:130`, проверка исходного графа около `509`. ScanAndValidate проверяет meta dependencies, затем processor добавляет новые связи без повторной валидации. Processor с неизвестным dependency ID дал `success=True, dependency count=1`. Следует проверять итоговый объединённый граф, включая отсутствующие узлы и циклы.

**C5 · P2 · Generated `_Path` конфликтует с допустимым именем другого asset.** `Karpik.Content.Codegen/ContentCodegenGenerator.cs:354–355`, резервирование имён `213,271`. Имена game/a и game/a_Path создают одновременно const Game_A_Path и asset field Game_A_Path. Текущий генератор в Roslyn-fixture выдал CS0102. Резервировать все генерируемые members, а не только asset fields.

## SDK, упаковка и конфигуратор

**S1 · P2 · Content-enabled SDK зависит от исходников репозитория вне NuGet-пакета.** `Karpik.Engine.Sdk/Sdk/Sdk.props:25,36`, `Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj`. Analyzer подключается ProjectReference через KarpikRepositoryRoot, tool собирается по пути на два уровня выше Sdk.props. В установленном пакете этих проектов нет; упаковка включает Core/Network analyzers и SDK tasks, но не Content tool/codegen. Внешний проект с KarpikContentEnabled=true и Content-каталогом не может пройти этот путь. Подтверждено исходниками targets и перечнем Pack items, отдельный чистый NuGet-consumer не запускался. Упаковать готовые Content-компоненты и использовать пути внутри пакета.

**S2 · P2 · Recovery удаляет staging другого активного упаковщика.** `Karpik.Engine.Tooling/AtomicDirectoryPublisher.cs:223–227`, вызов `Karpik.Engine.Packager/EnginePayloadBuilder.cs:33–38`. Каждый Build вызывает Recover, который считает обычный чужой `.owned` staging заброшенным. Изолированная компиляция текущих исходников: publisher A создаёт staging, B вызывает Recover, `Directory.Exists(A staging)` возвращает False. Затронуты и разные версии с общим output root. Нужен межпроцессный lease на всю транзакцию либо надёжное различение живых и брошенных транзакций. Потеря уже установленного payload этим сценарием не доказана.

**S3 · P2 · Configurator сохраняет профиль, который затем не открывает.** `Configurator/Program.cs:75–77`, предварительная валидация `16–20`. Включённый A зависит от B; выключить B, сохранить и выйти. SaveProfile не валидирует граф, следующая обычная Main возвращает 1 до интерактивного интерфейса. На текущих исходниках воспроизведён `Interactive restart exit code: 1`. Проверять перед сохранением и/или разрешать интерактивное исправление разобранного, но невалидного профиля.

## Тестовая инфраструктура

**B1 · P2 · StaticAnalyzer.Tests ссылается на удалённый путь Dragon.** `Tools/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj:23`. ProjectReference ведёт в `Dragon/Dragon.csproj`; актуальные исходники находятся в `third-parties/DragonECS`. Запуск тестового проекта останавливается на компиляции с отсутствующим DCFApixels. Исправить project reference и проверить фактическое имя проекта.

**B2 · P2 · Packager test ищет перемещённый SDK script в корне.** `Karpik.Engine.Packager.Tests/UpdateKarpikSdkScriptTests.cs:15`. Тест ожидает Update-KarpikSdk.ps1 в repository root, файл находится в `_scripts`. Воспроизведено падение Missing SDK update script. Исправить путь в тесте; отдельно проверять согласованность launcher/documentation ссылок при переносах.

## Сборка и тесты

Из 19 обнаруженных first-party тестовых проектов 16 завершились успешно: **727 passed, 18 skipped**. Четыре успешных теста прерванного Packager-run в эту сумму не включены. Эти результаты не покрывают все описанные edge cases.

| Тестовый проект | Passed | Skipped | Результат |
|---|---:|---:|---|
| Content.Tests | 45 | 0 | Успешно |
| Content.Runtime.Tests | 21 | 0 | Успешно |
| Network.Codegen.Tests | 24 | 0 | Успешно |
| Configurator.Tests | 13 | 0 | Успешно |
| ECS.Core.Tests | 34 | 0 | Успешно |
| Graphics.Core.Tests | 40 | 0 | Успешно |
| Editor.Tests | 92 | 8 | Успешно для выполненных тестов |
| Core.Generator.Tests | 49 | 0 | Успешно |
| Core.Runner.Tests | 128 | 0 | Успешно после повтора вне sandbox |
| ProjectModel.Tests | 31 | 0 | Успешно |
| Sdk.IntegrationTests | 14 | 6 | Успешно для выполненных тестов |
| Sdk.Tasks.Tests | 75 | 4 | Успешно для выполненных тестов |
| Tooling.Tests | 41 | 0 | Успешно |
| Jobs.Tests | 56 | 0 | Успешно |
| Launcher.Tests | 13 | 0 | Успешно |
| Memory.Tests | 51 | 0 | Успешно |
| StaticAnalyzer.Tests | — | — | Компиляция заблокирована B1 |
| Input.Tests | — | — | Нет части NuGet assemblies, включая PolySharp generator, в запуске --no-restore |
| Packager.Tests | 4 | 0 | 2 failed, затем hang timeout 120 s; полный набор не завершён |

Core.Runner первоначально имел 20 failed из-за недоступности named pipes в sandbox. Отдельная диагностика показала UnauthorizedAccess; повтор всего готового набора вне sandbox прошёл 128/128. Прежние падения не считаются дефектами runtime.

У Packager один failed — B2, другой получил отказ доступа к пользовательскому registry в проверке environment updater. Таймаут произошёл во время RepositoryModeIgnoresArbitraryStaleSourceBinAndModuleVersionDirectories; причина зависания не установлена, самостоятельным продуктовым дефектом не объявляется.

Полная сборка solution **не зелёная**: Directory.Build.targets:31 вызывает `dotnet run ... Configurator --validate` через Exec, где dotnet не распознан, MSB3073/9009 для Client.Publish и Server.Publish. Повтор вне sandbox и явное добавление расположения dotnet в Path не устранили проблему. Прямой запуск собранного Configurator с `--validate` вернул `Module graph validation passed`. Это установленное ограничение данной проверки, а не доказательство невалидности module graph. Первичная Avalonia telemetry ошибка доступа отдельно обходилась `-p:UsedAvaloniaProducts=`.

Есть NU1903 для Microsoft.Build.Utilities.Core 17.8.3. [GHSA-w3q9-fxm7-j8fq](https://github.com/advisories/GHSA-w3q9-fxm7-j8fq) описывает Linux DoS в MSBuild; применимость к продуктовым путям проекта не доказана. Windows-аудит не воспроизводит эту уязвимость. Обновление зависимости следует проверить отдельно; это не 35-я подтверждённая ошибка проекта.

Команды проверки: `dotnet build KarpikEngine.slnx --no-restore -m:1 -nr:false --verbosity quiet` и последовательные `dotnet test <project> --no-restore -m:1 -nr:false --verbosity quiet`; для ограниченного ожидания использовался `--blame-hang-timeout 120s`. Подробные локальные логи находятся в `.tmp/full-audit/`; они ignored и не являются долговременным репозиторным артефактом.

## Покрытие и ограничения

| Область | Что проверено | Граница вывода |
|---|---|---|
| Core/Runner/HotReload | Bootstrap, Runner, tick/lifecycle, IPC, state transfer, scheduling | Не полный анализ всех ProcessManager состояний |
| Core generators / Dragon integration | Registry, scheduling extraction static/dynamic, World/EventCallers, access graph | Весь static DI generator и upstream DragonECS построчно не проверены |
| Jobs/Memory/UnsafeUtilities | Scheduler, pool/handles, deque, await machinery, native containers/allocators, ArrayList | Без полного stress/benchmark прогона и всех DTO |
| Assets/Stats/Tween/Modding/Input | Основные lifecycle и mutation paths; findings выше | Многопоточные source proofs не равнозначны stress-test |
| Network/RPC | RpcGenerator, LiteNetLib handshake и integration boundaries; 24 codegen tests | Не security certification, не fuzzing произвольных сетевых пакетов |
| Physics/Graphics/Window | Aether world и fixed dt; GraphicsContext/OpenGL backend, converter gaps, SDL input | Без GPU, физической симуляции и cross-platform smoke |
| Content | Build coordinator, manifest/meta, registry/refs/leases, codegen, runtime tests | Аудит именно текущих незакоммиченных исходников |
| SDK/Tooling/Packager/ProjectModel/Configurator | Paths, validation, publication/recovery, package targets, model reader, interactive save | Packager suite прерван; внешний Content consumer проверен по package sources |
| Editor/Launcher/templates/scripts | Lifecycle/project opening/publish paths, SDK integration и существующие тесты | Не GUI end-to-end и не полный NativeAOT/platform matrix |
| Vendored библиотеки, samples, benchmarks | Интеграция и используемые границы | Не самостоятельный полный аудит upstream и каждого sample |

CBM применялся для структурного поиска и call chains; generation `2026-09-06T11:34:36Z`. Проверены coverage материализованных evidence paths и relevant scopes с пагинацией. Metadata_changed, not_tracked и parser gaps компенсировались чтением текущего исходника. Ложные эвристические связи графа для общих имён методов не использованы как доказательство. Чистый coverage означает только отсутствие зарегистрированного пропуска, а не отсутствие ошибок. Документированные контракты проверялись по локальным ADR/README; QMD был недоступен.

Jobs/Memory runtime-repros использовали существующие Debug DLL с дополнительной проверкой соответствующих механизмов в текущем исходнике. Generator и tooling fixtures компилировали текущие исходники. Подозрения, не доведённые до условий нарушения контракта (RPC playerEntity, некоторые physics handles/limits и editor static publish), не включены в 34 findings. Гипотеза потери старого Content artifact при неудачной публикации не подтвердилась в проверенном сценарии и также исключена.

## Какие знания стоит сохранить отдельно

Предлагаемые заметки для docs/knowledge, без автоматического создания: (1) поколения задач должны переживать переиспользование descriptor до завершения зависимостей; (2) recovery транзакции требует доказательства отсутствия живого владельца; (3) consumer input должен восстанавливаться после потери последнего события; (4) source generators обязаны учитывать наследование и все имена создаваемых членов; (5) версии ресурса должны различать повторную регистрацию manifest. Каждая тема подтверждается конкретными находками выше.
