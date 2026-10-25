# Lua ECS bindings: спецификация первого сквозного этапа

Дата: 2026-10-25, Europe/Samara. Статус: f32-срез реализован как экспериментальный путь; текущая проверка — в ExecPlan. Реализация поручена «Делай», работа в текущей ветке — явным «В текущей ветке делай». Push и PR не разрешены. См. также [принятый ADR](../docs/02_ADR/lua-ecs-common-bindings.md).

Связанные документы: [ExecPlan](lua-ecs-bindings-execplan.md), [подробные задачи](../docs/superpowers/plans/2026-10-25-lua-ecs-bindings.md), [проверки источников и probe](lua-ecs-bindings-evidence.md).

## Цель и наблюдаемый результат

Host регистрирует unmanaged CLR-компонент `Position : IEcsComponent` с публичными `float X, Y` под ID `game:Position`. При сборке генерируется прямой типизированный доступ к полям. Мод weather объявляет общий динамический `weather:Temperature`, версия 1, поле `Value: f32`. Его система выбирает сущности с Position без Temperature, читает Y и добавляет Temperature. После callback значение равно `(float)(20 - position.Y * 0.01)` по обычной числовой семантике Lua с проверкой f32 на границе записи. Следующий обход уже не выбирает обработанные сущности.

Общий слой не зависит от MoonSharp и доступен CLR-коду. Это не новое Lua-хранилище. Штатный `world.Where(out Aspect a)` остаётся CLR-механизмом; новый смешанный query не подменяет его.

## Проверенная база и границы задачи

По поручению пользователя от 2026-10-25 общий слой размещён в `Modules/Shared/Modding/Modding.Core/Bindings`, namespace `Karpik.Engine.Shared.Modding.Bindings`. Lua-адаптер находится в Modding.Lua; общий слой не зависит от MoonSharp.

Рабочая копия `C:\Users\artem\RiderProjects\KarpikEngine`, ветка `codex/modding-0.7`, HEAD `50b1d7586c92c1177a836d7a35e601736e2b4f6b`, чистая, ahead 7 по локальному origin tracking ref. Fetch не выполнялся. GitHub PR #101 проверен отдельно: открыт, draft, cooked assets, head той же ветки, remote head `74353a4d7a3799995001975881b0ce4b01ee3711`, base main. Не добавлять эти документы или реализацию к этому PR автоматически.

В baseline 50b1d75 были `LuaComponentRegistry.Register<T>(name)`, старые `reads/writes`, `callback(dt)`, per-instance descriptors и sequential `LuaUpdateSystem`. Настоящее изменение добавило динамические схемы, generated field bindings и этот query API; Lua reads/writes теперь диагностируются как требующие миграции. Исторические 216/216 из передачи не являются свежей проверкой.

Первый этап включает f32, плоский Position, Temperature, inc/exc/opt, has/get/add/del, reusable views, жизненный цикл, вывод scheduling accesses и обе политики ошибок. `ctx:create`, произвольные nested CLR-поля, пустые dynamic schemas, точные числа, enum и pointers остаются принятым продолжением, но не расширяют реализацию этого среза. Их нельзя молча эмулировать через object/boxing или объявлять отменёнными. Runtime должен диагностировать неподдержанный в срезе тип при подготовке, а не скрывать его публичные поля.

## Lua-контракт среза

```lua
return {
    components = {
        Temperature = { version = 1, fields = { Value = "f32" } }
    },
    systems = {
        {
            id = "initialize_temperature", phase = "update",
            queries = {
                pending = {
                    inc = { ["game:Position"] = {} },
                    exc = { ["weather:Temperature"] = { readonly = false } }
                }
            },
            callback = function(dt, ctx)
                local query = ctx.queries.pending
                local positions = query.components["game:Position"]
                local temperatures = query.components["weather:Temperature"]
                for entity in query:entities() do
                    local position = positions:get(entity)
                    local temperature = temperatures:add(entity)
                    temperature.Value = 20 - position.Y * 0.01
                end
            end
        }
    }
}
```

Таблицы components, queries и handles создаются при подготовке, не на сущность. `callback(dt, ctx)` получает Simulation-bound контекст, dt первым. Это существующая фаза Update с её текущим dt; сквозной тест не вводит новую физическую или gameplay-интеграцию с frame delta. FixedUpdate bindings — отдельное продолжение.

ID динамической схемы — `<modId>:<localName>`. Версия — положительное целое. Имена полей сравниваются ordinal, порядок ключей Lua-таблицы не определяет schema identity: layout канонизируется по имени. Повторный полный ID принимается только при совпадении версии и всего дерева полей. Чужой мод использует полный ID без повторной декларации. Схемы и версии фиксируются в ModSet до pipeline; неизвестный ID в любом фильтре — ошибка подготовки с mod/system/query/component ID.

`inc` требует все указанные компоненты, `exc` исключает при наличии любого, `opt` не ограничивает выборку. `readonly` по умолчанию true; только boolean false даёт mutable. Mode не зависит от фильтра: exc mutable разрешает add исключённого компонента выбранной сущности. Query names независимы. Неизвестные ключи/формы описаний отклоняются; один ID в нескольких группах одной query отклоняется как неоднозначное объявление. Один ID в разных queries допустим с разными правами.

Списки Lua `reads/writes` заменяются queries. В новом формате их присутствие — диагностическая ошибка с указанием миграции, а не дополнительный канал полномочий. `before/after` и полные ID систем сохраняются. Пустой `systems` продолжает запрещать legacy `on_update` во всех entry points мода. Legacy-моды без systems сохраняют текущее поведение. Named queries могут отсутствовать: тогда ctx.queries пуст и доступ к ECS не выдан.

## Пулы, ID и views

`entity` — обычный int-ID, выдаваемый как Lua number, без entity wrappers, userdata и обязательного entlong. Сохранённый ID может после удаления указывать на другую сущность: поколение пользовательскому ID не добавляется.

`query.components[id]` содержит подготовленный handle именно этой query. Доступ через opt возможен без вызова entities; фильтр не запрещает работу с другой живой сущностью того же мира.

| Операция | Поведение |
| --- | --- |
| `has(id)` | Проверка присутствия в подготовленном пуле |
| `get(id)` | Переиспользуемый view; отсутствие компонента — ошибка |
| `add(id)` | Только mutable, default-значение, существующий компонент — ошибка; возвращает reusable view |
| `del(id)` | Только mutable; отсутствие — ошибка; обычное удаление пустой сущности сохраняется |

Lua-граница всегда проверяет число на целочисленность, диапазон int, допустимый индекс, принадлежность живому миру и наличие перед доступом к native-пулу. Нельзя полагаться на DEBUG-проверки DragonECS: они убраны в Release. Для корректного живого ID без компонента has возвращает false; для невалидного/мёртвого ID операции дают ошибку callback.

Mutable разрешает чтение и запись; readonly-view запрещает setter, add и del. Следующий get/add через тот же handle передвигает его view на другой entityId. View — не снимок, не держит ref или адрес элемента между вызовами. Каждый getter/setter заново проверяет владельца и присутствие компонента. После dense swap либо расширения массива он адресует правильную сущность через sparse mapping.

Все handles, views и queries принадлежат системе и Simulation. Их можно сохранить до следующего её callback, но использовать вне активного callback или из другой системы той же VM нельзя. Callback gate содержит владельца и активный invocation; в finally снимает доступ и сбрасывает курсоры даже при ошибке. Readonly и mutable handles одного component ID в разных queries не сливаются в один mutable view.

## Снимок выборки и рост

Снимок — массив int-ID плюс длина, принадлежащий именованной query. На `entities()` выбирается наименьший обязательный inc-пул, native либо dynamic, и проверяются остальные inc/exc. При пустом inc источник — живые сущности мира. Opt не влияет. Ссылку на Dragon EcsSpan нельзя сохранять между Lua-операциями: она инвалидируется структурными изменениями.

Снимок полностью материализуется ДО возврата iterator в Lua. Новые сущности, add/del и изменение membership не меняют этот набор ID; данные читаются актуальные. Если выбранная сущность удалена, iterator всё ещё выдаёт её сохранённый ID, а дальнейшие pool-операции проверяют текущую живость. Защиты от повторного использования ID нет, как согласовано. Стабильного порядка нет.

Разные named queries имеют разные буферы и курсоры, поэтому допускают вложенные циклы. Перед каждым callback курсоры сбрасываются. Инженерное уточнение для одного имени: повторный entities() перезапускает его единственный курсор и заменяет снимок; независимый вложенный обход той же query не обещается, для него используются два имени. Это уточнение не требует ручных batch loops.

На старте snapshot buffers резервируются на текущую world.Capacity, затем увеличиваются по OnWorldResize с сохранением содержимого и прежней длины уже активного снимка. Iterator обращается к полю query, а не удерживает старый массив. Рост другой query не может перезаписать активный буфер. Стоимость O(queryCount * world.Capacity) int-ячеек документируется; hard limits не вводятся. Это отдельная ёмкость результатов, не аллокация всех динамических полей на world.Capacity.

## Владение данными и lifecycle

Engine хранит только неизменяемые CLR bindings и регистрации имён. ModSet владеет фиксированным каталогом schema ID/version/layout. Simulation владеет общим storage всех модов, native world, query state и VM по одной на мод.

Первая Simulation исполняет настоящие entry points, собирает компоненты всех модов, валидирует весь набор, атомарно фиксирует ModSet registry и лишь затем разрешает query handles и строит pipeline. Вторую VM ради схем не создавать. Последующие Simulation создают новые VM и сравнивают полный собранный набор со snapshot ModSet; изменение схем требует нового ModSet. Подготовки конкурирующих Simulation сериализуются только на startup commit каталога; callback не берёт lock. Ошибка/отмена до commit не оставляет частично зафиксированный каталог.

На схему — один общий SoA-пул: float[] Value для Temperature, int[] denseEntityIds, int[] sparse с sentinel -1. Dense removal — swap с последним и исправление sparse перемещённой сущности. Sparse растёт по world.Capacity; dense data — от PoolComponentsCapacity с увеличением степенями двойки по заполнению, с проверкой overflow. Аллокации роста не маскируются как steady state. Значение добавляемого Temperature всегда default(float), включая повторное использование освобождённого слота.

Служебный `DynamicComponentAnchor : struct, IEcsComponent` существует ровно при dynamic-count(entity)>0 суммарно по всем модам. Первый dynamic add добавляет его, последний dynamic del удаляет. При удалении последнего native-компонента, если dynamic ещё есть, anchor сохраняет сущность. Он не является игровым маркером запроса удаления.

Обычный dynamic del сначала исправляет динамические mapping/count, затем при count=0 удаляет anchor через штатный пул. Не вызывает DelEntity вручную: пустую сущность обрабатывает DragonECS. В world `OnReleaseDelEntityBuffer` native-пулы уже очищены, ID ещё не возвращены dispenser. Здесь storage очищает все динамические записи и counts, НЕ вызывает anchor.Del/DelEntity и не инициирует рекурсивное удаление. До release внешние записи могут физически существовать, но `world.IsUsed(id)==false` запрещает доступ к ним. Resize/destroy/listener subscription имеют Simulation lifetime, Dispose идемпотентен и отсоединяет listener.

Прямого Lua delete-entity API нет. Согласованный запрос через игровой marker и обработка CLR-системой в конце tick остаются вне этого f32-среза; source-проверка не нашла готовую такую систему в ECS.Core, поэтому называть её существующей реализацией нельзя. Lifecycle тест использует native world.DelEntity. Cross-world migration внешних динамических данных не реализуется этим этапом и требует отдельного контракта.

## Генерация и общий C# слой: выбранное инженерное предложение

Ниже имена и размещение будущих внутренних API; они не являются уже принятым публичным C# API для игры. Расположение использует существующие проекты, без новой сборки и новой зависимости:

| Место | Ответственность |
| --- | --- |
| `Modules/Shared/Modding/Modding.Core/Bindings/` | Независимые от VM registries, typed fields, native adapters, dynamic SoA, prepared queries, anchor/lifecycle |
| `Modules/Shared/Modding/Modding.Lua/` | Парсинг Lua, owner gate, descriptors/views/iterator для MoonSharp, error policy boundary |
| `src/Karpik.Engine.Core/Scheduling/` | Общая identity доступа и нормализация numeric keys при построении графа |
| `src/Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/` | Сбор Register<T>, typed CLR field access, provider emission и static composition integration |

`IComponentPoolAccess` предоставляет Count, Has(int), Add(int), Del(int), CopyEntityIds(Span<int>). Native adapter использует EcsPool<T>.ToSpan и копирует int-индексы в подготовленный буфер, без native Enumerator.Current/entlong. `IComponentField<TValue>` предоставляет `TValue Read(int entityId)` и `void Write(int entityId, TValue value)`. В срезе TValue=float; вызов интерфейса не упаковывает float в object. Native implementation получает ref/ref readonly T при каждой операции; generated getter/setter обращается прямо к X/Y. Readonly-ограничение handle проверяется до field.Write. Readonly CLR fields остаются только читаемыми независимо от режима handle.

`ClrComponentBindings.Register<T>(string id)` сохраняет Engine metadata с generated factory. `IClrComponentBindingProvider.GetBindings()` возвращает ReadOnlyMemory<ClrComponentBindingDescriptor>; descriptor содержит Type и typed factory `IComponentPoolAccess Bind(EcsDefaultWorld world)`. Bound pool предоставляет Fields: ReadOnlyMemory<ComponentFieldDescriptor>; каждый field descriptor содержит bound f32 accessor, имя и CanWrite. Поля привязываются при подготовке Simulation, это не object-значения компонента. Регистрируется только закрытый unmanaged T : IEcsComponent. Неподдержанный/недоступный тип диагностируется до callback. Существующий LuaComponentRegistry становится тонкой совместимой точкой входа, перенаправляющей регистрацию в общий registry; старые Register<T> callers не требуют ручных field allowlists.

Генератор семантически распознаёт Register<T> общего registry и существующего Lua facade в исходниках сборки вызова; один binding на конкретный T. Внешний публичный Position из referenced assembly допустим; private/inaccessible T должен дать понятную диагностику. Имя ID остаётся аргументом runtime Register и не обязано быть compile-time константой. Открытый T в generic forwarding-методе нельзя угадывать: emit diagnostic, caller предоставляет закрытую регистрацию. Runtime-only DLL без заранее сгенерированных bindings не получает reflection fallback.

Generated provider — публичный Engine-scoped export с пустым конструктором; модуль получает generator через существующий Directory.Build.props. Для сторонней сборки вне Modules подключение analyzer задаётся явно в её build. Dynamic использует обычное discovery экспортов загруженных assemblies только при старте. Static получает provider из referenced compiled assembly и typed factory; reflection scan в Static запрещён.

Не рассчитывать, что один Roslyn generator увидит output другого в том же запуске. Для Register<T> в исходниках самого static host binding-модель передаётся прямо в RuntimeCompositionGenerator и он явно добавляет generated provider к registrations. Для providers из уже собранных referenced assemblies достаточно существующего ScanCore, но его memoization key и side-filter нужно проверить новыми multi-assembly tests. Это интеграция генерации, не новый runtime discovery service.

`ComponentSchemaRegistry` (ModSet) хранит канонические `ComponentSchema` records, `DynamicComponentStorage` (Simulation) предоставляет `IComponentPoolAccess Resolve(schemaId)`. `PreparedComponentQuery.Capture()` заполняет собственный буфер; `bool MoveNext(out int id)` отдаёт int. Lua adapter кеширует VM-visible values для handles и views при подготовке. Публичный удобный CLR query API пока не вводится: тесты common-механизма вызывают эти внутренние контракты напрямую.

## Планировщик

Текущий EcsComponentAccessDescriptor принимает Type, граф уже превращает CLR Types в int keys при подготовке. Расширить identity до различимых CLR Type / dynamic schema ID; не использовать фиктивные CLR-типы на схему. Старый конструктор `(Type, EcsAccessMode)` и C# attributes/generated declarations сохраняются.

Инженерный вариант: новый readonly `EcsComponentAccessKey` с CLR Type либо dynamic ID, equality без совпадений между вариантами; новый overload descriptor(key, mode), свойство Key. Прежний ComponentType остаётся для CLR и явно отклоняет чтение для dynamic; обновить внутренние consumers на Key. Граф публикует общий AccessKeys, ComponentTypes сохраняется как CLR-only compatibility metadata; numeric индексы read/write массивов относятся к AccessKeys. Это нужно зафиксировать тестом, не смешивать два индексных пространства. Ключи детерминированно нормализуются один раз, не разрешаются строками в каждом кадре.

Union всех inc/exc/opt системы формирует Read/Write: default readonly -> Read, mutable -> Write, Write доминирует. Это включает presence filters. Readonly handles не повышаются при merge accesses. Две схемы разных модов различаются полным ID; разные имена одного CLR Type разделяют один scheduling resource.

Служебные операции anchor учитываются отдельным CLR Write доступа системы с mutable dynamic pool; это выведенная техническая зависимость, не новое разрешение Lua и не отдельный режим structural write. LuaUpdateSystem остаётся sequential. В текущем GraphBuilder IsSequential конфликтует со всеми соседними системами, поэтому первая версия консервативно сериализует Lua и native работу. Tests графа с synthetic non-sequential descriptors должны отдельно доказать dynamic Read/Write: sequential-флаг иначе скроет отсутствие нужных accesses.

## Числа и границы полей

Position.X/Y и Temperature.Value — f32. Чтение переводит float в Lua double без дополнительной потери; запись округляет double до float. Если конечное double превращается в float Infinity, запись даёт ошибку до изменения поля. Уже заданные NaN и ±Infinity разрешены и сохраняются. Строки, bool, nil не являются числом для field setter. Не использовать object, reflection GetValue/SetValue или boxed struct.

Следующий общий каталог должен перечислить bool, целые, f32/f64, char, decimal, enum, nested unmanaged, fixed buffers и pointers с явным статусом. Сейчас подтверждён только контракт f32 этого среза. i64/u64/decimal конструируются точно из строк и считают без double; смешивание точных типов требует явного преобразования; обычный Lua number допускается только без потери уже полученного значения. Enum — underlying integer с проверкой диапазона без Enum.IsDefined. Pointers — opaque round-trip без arithmetic/dereference. Эти решения сохраняются, но их стоимость и реализации здесь не доказаны.

## Ошибки и совместимость

Host выбирает `ScriptSystemErrorPolicy.StopSimulation` (default) либо обязательный `LogAndContinue`. Enum и ScriptSystemOptions находятся в Modding.Core и не зависят от MoonSharp. Политика охватывает ошибки активного declared callback, включая bindings; не проглатывает startup-validation, отмену и ошибки инфраструктуры вне callback. StopSimulation прерывает текущий update и доводит ошибку до host stop path; LogAndContinue логирует mod/system/query context, обрывает только callback, вызывает следующие системы и повторяет ошибочную систему в следующем кадре. Автоматического disable нет. Политика задаётся через DI/options Simulation, не статическое игровое состояние.

Все записи прямые. Ошибка не откатывает уже выполненные add/del и поля. Исключение setter до присваивания сохраняет предыдущее значение; уже созданные компоненты остаются. Gate/cursor cleanup выполняется finally в обеих политиках. Ошибки подготовки останавливают запуск независимо от выбранной политики.

Lua-моды с systems/reads/writes требуют миграции на queries; это явное изменение формата. Смена schema version требует нового ModSet. CLR scheduling attributes и старый Lua legacy lifecycle сохраняются. Не включать cooked assets, hot reload схем, сериализацию динамических данных и сетевую репликацию в этот этап.

## Real-time и измеримый допуск

Цель общего C# слоя после reserve/warmup — 0 managed B/callback и B/entity для capture, pool/field access и ownership checks; без LINQ, IEnumerator boxing, closures на итерацию и locks в callback. Общие данные SoA; имена разрешаются один раз. Рост ёмкости и startup считаются отдельно. Одна VM не исполняется параллельно.

Свежий отдельный probe на локальной DLL: .NET 10.0.11, MoonSharp 2.0.0.0, DynValue — class; NewNumber=48 B/value. Числовой цикл без ECS дал N=0:752, N=1:992, N=16:4592, N=256:62192, N=4096:983792 B/callback. Для этого конкретного выражения наблюдается 752+240*N; это НЕ формула стоимости bindings и не допустимый бюджет проекта.

До реализации adapters первый будущий milestone измеряет отдельно: empty callback(dt), empty callback(dt,ctx), raw Lua loop, выдачу int iterator, field read, add, setter, полное выражение; N=0/1/16/256/4096, reserved и forced-growth сценарии. Метрики: B/callback, дополнительный B/entity, B/read/write/add, время p50/p95/p99, число collections; сборка Release, warmup 2000, не менее 1000 измеряемых callbacks, логирование/форматирование вне интервала. Для add заново готовить исходное membership вне измеряемого callback, иначе следующие вызовы измерят пустую query.

Зафиксирована фактическая модель actual ECS `B(N)=1984+1464*N` bytes/callback. Pure common capture/pool/field access после reserve дал 0 managed bytes. После T0 пользователь явно разрешил экспериментальный потолок `4096+1600*N` bytes/callback; это измеряемое исключение только для данного MoonSharp-пути, не production-ready обещание. Ceiling проверяется Release allocation tests. Полная матрица и ограничения измерений — в evidence и ADR.

## Приёмка

Матрица тестов и команды находятся также в ExecPlan. Обязательны: точный mixed result и значения; права и неизвестные ID до кадра; native/dynamic sources и no-inc; snapshot при membership change/росте; nested named queries; ownership между системами/вне callback/после dispose; f32 NaN/Infinity/overflow; duplicate add/missing get/del; swap-remove и default повторного слота; последний dynamic component и anchor; native deletion до/после release и повторный ID; новый Simulation и неизменяемый ModSet; обе error policies и partial writes; Static/Dynamic composition × Deterministic/Parallel scheduler; synthetic graph conflicts независимо от sequential; измеренный allocation budget.

Экспериментальный f32-срез реализован и прошёл автоматическую приёмку; фактические результаты и ограничения находятся в ExecPlan. Ручная игра/NativeAOT не объявляются проверенными; более широкий каталог типов и операций остаётся следующим этапом.
