# Lua ECS bindings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans after an explicit implementation request. Steps use checkbox syntax. This document does not authorize execution or delegation.

**Goal:** Реализовать только сквозной f32-срез Position → mixed query → Lua → Temperature.

**Architecture:** VM-independent bindings в Modding.Core, immutable ModSet schemas и Simulation-owned SoA. Lua adapter выдаёт int-ID и reusable views; scheduler нормализует CLR/dynamic access keys до кадра.

**Tech Stack:** C#/.NET 10, Roslyn incremental generator, DragonECS из checkout, MoonSharp 2.0.0.0, существующие xUnit projects и Autofac/static composition.

**Spec:** [Спецификация](../../../plans/lua-ecs-bindings-spec.md). **Overview:** [ExecPlan](../../../plans/lua-ecs-bindings-execplan.md). **Evidence:** [Audit](../../../plans/lua-ecs-bindings-evidence.md).

## Global Constraints

- Реализация явно поручена «Делай». Никаких push/PR автоматически.
- Пользователь явно разрешил текущую codex/modding-0.7; новую ветку/worktree не создавать, PR #101 не публиковать/не менять.
- Common runtime не зависит от MoonSharp; нет object/boxing для значений, entity wrappers, reflection field access, ручных batch loops.
- Integer entity ID без generation protection; callback(dt,ctx); inc/exc/opt; readonly=true по умолчанию.
- Direct writes без rollback; StopSimulation default и обязательный LogAndContinue без auto-disable.
- Common C# steady-state allocation target 0 B; разрешён экспериментальный VM ceiling 4096+1600*N B/callback после reserve, zero-GC Lua не обещается.
- Проверка каждого milestone до следующего. Build/test команды single-node `-m:1 -nr:false`.
- Third-parties/DragonECS не патчить. Не добавлять dependencies/новую сборку ради этого среза.

## Review Focus

- Release native pool API не валидирует все ошибочные IDs: trust boundary tests в T3/T5.
- Sequential скрывает потерянные dynamic accesses: synthetic non-sequential graph tests в T4.
- Generator не увидит output другого generator same-run: own-source и referenced-assembly compiled fixtures в T1.
- Resize и swap-remove инвалидируют spans/refs, но snapshot/view обязан остаться корректным: T2/T3.
- LogAndContinue после частичной записи должен сохранить данные и повторить callback в следующем кадре; StopSimulation должен дойти до host: T5.

## Files and Interfaces

Все пути далее repository-relative, будущие Create ещё не существуют. Имена внутренних контрактов — выбранное инженерное предложение этого пакета. Публичный игровой CLR query API не добавляется. Использовать namespace `Karpik.Engine.Shared.Modding.Bindings` для common API.

`IComponentPoolAccess`: `int Count { get; }`, `bool Has(int entityId)`, `void Add(int entityId)`, `void Del(int entityId)`, `int CopyEntityIds(Span<int> destination)`, `ReadOnlyMemory<ComponentFieldDescriptor> Fields { get; }`. CopyEntityIds требует destination.Length>=Count, возвращает число скопированных int; от источника не остаётся borrowed span.

`IComponentField<TValue>`: `TValue Read(int entityId)`, `void Write(int entityId, TValue value)`. Для этого этапа bound `ComponentFieldDescriptor` содержит `string Name`, `bool CanWrite`, `IComponentField<float> F32`; descriptor создаётся на bind, не на сущность. Все поля среза f32; неподдержанные типы отклоняются при регистрации. Readonly-field accessor.Write всегда бросает до изменения.

`ClrComponentBindingDescriptor`: `Type ComponentType`, factory `IComponentPoolAccess Bind(EcsDefaultWorld world)`; `IClrComponentBindingProvider.GetBindings(): ReadOnlyMemory<ClrComponentBindingDescriptor>`. Descriptor — immutable metadata, Bind возвращает Simulation-bound adapter и поля. `ClrComponentBindings.Register<T>(string id)` принимает unmanaged IEcsComponent, `Resolve(string id)` возвращает binding descriptor; Seal закрывает регистрации. Provider factories создают объекты только при подготовке.

`ComponentSchema(string Id, int Version, params SchemaField[] Fields)` и `SchemaField(string Name, SchemaScalarKind Kind)` в срезе плоские, Kind=F32. Registry копирует descriptions, сравнивает канонический набор всех модов; nested support сейчас не писать. `ComponentSchemaRegistry.ValidateAndFreeze(ReadOnlySpan<ComponentSchema> schemas)` атомарно фиксирует либо сравнивает полный набор; `GetSchemas()` отдаёт независимую глубокую копию metadata arrays.

`DynamicComponentStorage.Resolve(string schemaId): IComponentPoolAccess`; данные и listener один на Simulation. Prepared adapter validation доверяет только собственному common callback gate, не проверкам DEBUG native library.

`ComponentQueryDefinition` хранит имя и три immutable массива `(componentId, readonly)`; `ComponentQueryPreparer.Prepare(definition, ownerId)` разрешает handles один раз и возвращает `PreparedComponentQuery`. У него `Capture()`, `Reset()`, `bool MoveNext(out int entityId)` и подготовленные component handles. `ComponentCallbackScope.Enter(ownerId) / Exit()` устанавливают владельца; не вводить IDisposable struct, который случайно box-ится в Lua. Gate и finally вызываются host adapter.

`EcsComponentAccessKey.ForClr(Type)` / `.ForDynamic(string)` формируют различимые metadata keys; descriptor получает overload `(key, mode)` при сохранении `(Type,mode)`. Граф один раз строит общий AccessKeys и int read/write ids. Старый ComponentTypes остаётся CLR-only projection; не использовать его для индексации mixed accesses.

`ScriptSystemErrorPolicy { StopSimulation, LogAndContinue }` и immutable Simulation-scoped options задаются host через DI. Lua Update adapter ловит только ошибку текущего callback: policy determines log/continue или rethrow, cleanup в finally; startup errors не проходят этот catch boundary. Не добавлять locator для ILogger/options/gate.

## T0 / M0: разрешения, воспроизводимость и allocation gate

**Files:** после разрешения Create `tests/Karpik.Engine.Core.Runner.Tests/LuaEcsAllocationTests.cs`; external probe остаётся reference baseline. Production bindings в T0 не писать.

**Consumes:** текущая DLL MoonSharp и контракт spec. **Produces:** измеренная таблица B0/b, boundary costs, latency и конкретное решение разработчика о допустимом budget.

- [x] Проверить status/HEAD/PR заново. Допустимый путь — новая codex/lua-ecs-bindings от актуального main после попадания необходимых prerequisites, либо явно согласованная dependent ветка с записанным parent/base. Не создавать ветку от task-ветки по умолчанию. Перенести spec/ExecPlan/tasks и исправить ссылки только на допустимой базе.
- [x] Получить явное поручение на реализацию: согласование пакета планов не равно поручению писать runtime/codegen.
- [x] Создать test-only probes MoonSharp: EmptyDt, EmptyDtAndContext, RawNumericLoop, PreparedIntIterator, PreparedFloatReadWrite, ReturnSameView. Последние probes — изолированные persistent test descriptors, не production pool/storage. Проверки: число выданных ID равно N, значение float round-trip равно записанному, повторный get возвращает тот же host view; не измерять неверную пустую функцию вместо API boundary.
- [x] Выполнить Release allocation suite по N=0/1/16/256/4096, warmup=2000, measured>=1000; отдельно no-growth/growth, no lookup/reflection/logging в интервале. Сохранить таблицу B/call, B/entity, B/get/set/add, p50/p95/p99 и collections; любые compiler/probe failures исправить до выводов.
- [x] Сверить lower bounds с fresh raw probe (752+240*N только для его body), не приравнивать их. Передать конкретный total/added budget на решение разработчика. Пока численный бюджет не принят, дальнейшие T1–T5 не исполнять. Никакой замены VM или циклов под видом оптимизации.

**Validation:** cwd repository root; `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false --filter FullyQualifiedName~LuaEcsAllocation`. Ожидается ненулевое число tests и воспроизводимая таблица; approval budget — отдельный recorded result, не pass теста.

## T1 / M1: common CLR bindings и генерация Register<T>

**Create:** `Modules/Shared/Modding/Modding.Core/Bindings/ComponentFieldDescriptor.cs`, `IComponentPoolAccess.cs`, `IComponentField.cs`, `ClrComponentBindings.cs`, `IClrComponentBindingProvider.cs`; `src/Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/ClrComponentBindingGenerator.cs`; `tests/Karpik.Engine.Core.Generator.Tests/ClrComponentBindingGeneratorTests.cs`.

**Modify:** `Modules/Shared/Modding/Modding.Lua/LuaComponentRegistry.cs`; `RuntimeCompositionGenerator.cs` в той же generator папке; `tests/Karpik.Engine.Core.Generator.Tests/RuntimeCompositionGeneratorTests.cs`; direct-construction registry fixtures в `tests/Karpik.Engine.Core.Runner.Tests/LuaSystemRegistrationTests.cs` и `LuaPreparationTests.cs`. При необходимости analyzer ProjectReference только в новом multi-assembly fixture вне Modules; существующий глобальный Modules include не дублировать.

**Consumes:** common contracts выше и существующие static/dynamic registration пути. **Produces:** typed Position adapter, generated provider и общий Engine registry; существующий LuaComponentRegistry.Register<T> остаётся совместимой host-точкой входа.

- [x] Добавить failing tests `ClrComponentBinding_GeneratesRefFloatAccess`, `ClrComponentBinding_ReadonlyFieldCannotWrite`, `ClrComponentBinding_RejectsUnsupportedPublicField`, `ClrComponentBinding_ExportsPublicInstanceFieldsOnly`. Assertions: X/Y читаются и пишутся прямо в native pool; static/property/private не экспортируются; readonly CanWrite=false и setter throws; unsupported field даёт диагностическую ошибку, а не silent omission. Запустить фильтр ClrComponentBinding, подтвердить ожидаемую причину RED.
- [x] Добавить compiled fixtures `ClrComponentBinding_ReferencedAssemblyProvider`, `ClrComponentBinding_OwnStaticHostProvider`, `ClrComponentBinding_GenericForwardingDiagnostic`. Build referenced Position assembly с регистрацией в другой доступной assembly; выполнить generated getter, не только string contains source. Проверить Dynamic/Static один provider на T, Client/Server side parity, modified referenced assembly обновляет кэш, Reflection в Static не добавилась.
- [x] Реализовать binding discovery по semantic symbol Register<T> с закрытым T и generated typed accessor, native ref на операцию, без boxing/reflection. Для собственной static compilation передать binding models непосредственно в RuntimeCompositionGenerator; для compiled references использовать публичный generated provider-export. Не ожидать same-pass generated symbol discovery.
- [x] Реализовать ClrComponentBindings через constructor-injected providers; перенаправить существующий Lua facade в common registry и обновить direct test fixtures. Зарегистрировать через Export+ServiceRegistration Engine; native bound adapter создаётся factory в Simulation preparation, не хранится Engine registry.
- [x] Запустить Generator фильтр, затем relevant LuaRegistration/Preparation tests; зафиксировать число tests, exit code и отсутствие пустого фильтра. Набор функций с неподдержанными типами явно падает на подготовке, не меняет соглашения о будущих типах.

**Validation:** Generator command из ExecPlan; Runner `--filter 'FullyQualifiedName~LuaSystemRegistration|FullyQualifiedName~LuaPreparation'`, `-m:1 -nr:false`. До GREEN не начинать T2.

## T2 / M1: ModSet schema registry, dynamic SoA и lifecycle

**Create:** `Modules/Shared/Modding/Modding.Core/Bindings/ComponentSchemaRegistry.cs`, `DynamicComponentStorage.cs`, `DynamicF32ComponentPool.cs`, `DynamicComponentAnchor.cs`; `tests/Karpik.Engine.Core.Runner.Tests/DynamicComponentStorageTests.cs`.

**Consumes:** T1 pool/field contracts и EcsDefaultWorld public lifecycle hooks. **Produces:** общий Temperature pool для любого runtime, atomic schemas и корректный anchor, без Lua dependency.

- [x] Написать failing tests `DynamicComponent_DefaultSwapRemoveAndSlotReuse`, `DynamicComponent_AnchorExistsIffAnySchemaPresent`, `DynamicComponent_DeletionReleasePrecedesIdReuse`, `DynamicComponent_LastDynamicRemovesAnchor`, `DynamicComponent_ResizeAndDestroyAreSafe`, `DynamicComponent_ModSetFreezeAndSimulationIsolation`. Assertions: default=0; после удаления middle элемента значения остальных по entity ID прежние; две схемы разных модов сохраняют anchor до последнего del; native DelEntity делает binding недоступным до release, partial/full release очищает до повторного ID; повторный Dispose безопасен; schema order не важен, version/field drift отклоняется. Выполнить фильтр DynamicComponent и зафиксировать RED.
- [x] Реализовать ComponentSchemaRegistry immutable/canonical compare; commit всего набора атомарен, cancellation/failure до commit не оставляет partial data. Первый successful freeze сериализован на startup; следующие concurrent prepares валидируют frozen snapshot, без lock в callback.
- [x] Реализовать sparse=-1, dense entity IDs, float column, count всех схем на entity. Sparse resize по world, dense from PoolComponentsCapacity с pow2 growth, overflow checks. Bind field accessor на Simulation pool; default при каждом add.
- [x] Подключить public world resize/release/destroy listener в Simulation. Normal first/last dynamic add/del управляет anchor. Release cleanup не вызывает native Del/DelEntity повторно, потому что native pools уже очищены. Отписываться при dispose; закрыть add/get/del после destroy.
- [x] Выполнить targeted tests, включая Release, random seed 20261025 на 1000 add/del/resize последовательностей против простой model-map В ТЕСТЕ. Проверять count/membership/value/anchor после каждой операции. Записать GREEN либо конкретное ограничение; dictionary model не переносить в production hot path.

**Validation:** Runner command с `--filter FullyQualifiedName~DynamicComponent`, затем та же команда `-c Release`. Не менять native library ради теста.

## T3 / M2: mixed query, права и snapshot ownership

**Create:** `Modules/Shared/Modding/Modding.Core/Bindings/ComponentQueryDefinition.cs`, `ComponentQueryPreparer.cs`, `PreparedComponentQuery.cs`, `PreparedComponentHandle.cs`, `ComponentCallbackScope.cs`; `tests/Karpik.Engine.Core.Runner.Tests/MixedComponentQueryTests.cs`.

**Consumes:** T1/T2 native/dynamic pools. **Produces:** общий prepared query, per-query handles, int snapshots и callback owner gate.

- [x] Написать failing `MixedComponentQuery_SelectsPositionWithoutTemperature`, `MixedComponentQuery_DynamicOrNativeSmallestInc`, `MixedComponentQuery_OptAndEmptyInc`, `MixedComponentQuery_SnapshotAndNestedNames`, `MixedComponentQuery_GrowthPreservesActiveSnapshot`, `MixedComponentQuery_RejectsWrongOwnerAndInvalidIds`. Проверять exact sets, readonly default/mutable exc, opt pool без capture, unknown inc/exc/opt, Release отрицательные/дробные/out-of-range/dead IDs на Lua boundary позднее T5. Сначала common tests получают int invalid bounds. Повторный Capture одного имени перезапускает cursor, два имени не делят buffer. Запустить и записать RED.
- [x] Подготовить имена в handles один раз. Выбор smallest inc, полное копирование candidates и фильтрация до возвращения Lua iterator. No-inc использует живой world source. Snapshot принадлежит named query; после capture add/del не пересчитывают его до нового вызова.
- [x] Реализовать owner gate и reusable field view со сменой entityId при get/add. Поля проверяют presence/owner каждый раз и не держат ref через операции. Проверить readonly-view cannot write даже если другая query имеет mutable handle того же типа; union scheduling не расширяет права.
- [x] Подключить snapshot reserve и resize с prefix copy; iterator использует query object, старый массив не захватывает. После break/error следующий callback reset корректен. Чтение нового живого значения через старый ID подчиняется договорённому int reuse; generation protection не добавлять.
- [x] Запустить common tests и C# allocation self-check на warmed/reserved capture/Read/Write: 0 measured managed bytes за 1000 iterations. Форматирование и assertions вне интервала. Проверить forced growth отдельно, без требования 0 B.

**Validation:** Runner `--filter FullyQualifiedName~MixedComponentQuery`, Debug/Release single-node. Общий слой не ссылается на MoonSharp: это проверяется project reference/source dependency assertion.

## T4 / M2: общие scheduling resource keys

**Create:** `src/Karpik.Engine.Core/Scheduling/EcsComponentAccessKey.cs`.

**Modify:** там же `EcsComponentAccessDescriptor.cs`, `EcsUpdateGraphBuilder.cs`, `EcsUpdateGraph.cs`; `tests/ECS.Core.Tests/EcsUpdateGraphBuilderTests.cs`; `ComponentQueryPreparer.cs` для выведенного union accesses. `EcsUpdateRegistryGenerator.cs` сохраняет текущую Type-constructor emission; менять только если compilation test обнаружит необходимую совместимость, не переписывать генерацию native attributes.

**Consumes:** query definitions и EcsAccessMode. **Produces:** общие int access keys у native/dynamic systems, неизменные CLR attributes и Type ctor.

- [x] Добавить failing tests `MixedKeys_ReadReadNoEdge`, `MixedKeys_ReadWriteAndWriteWriteEdge`, `MixedKeys_ClrAliasesShareResource`, `MixedKeys_DynamicIdsAreDistinct`, `MixedKeys_BothBuildPathsAndCompatibilityProjection`. Использовать synthetic IsSequential=false, чтобы получить RED при отсутствии dynamic accesses. Проверить readonly exc presence=Read, mutable exc=Write, Write dominates across queries, derived anchor Write, CLR Type и dynamic string никогда не совпадают.
- [x] Реализовать key discrimination и нормализацию в обоих Build/BuildInstances до кадра; не генерировать surrogate CLR Type на dynamic schema. Старый descriptor(Type,mode) остаётся; mixed consumers используют Key. Graph.AccessKeys соответствует int read/write indices; graph.ComponentTypes — только compatibility projection.
- [x] Сохранить детерминированность, before/after/cycle validation, legacy native metadata и sequential fences. Выполнить targeted графовые tests и existing full ECS.Core.Tests, затем Generator regression на native descriptors.

**Validation:** ECS.Core command из ExecPlan; expected 0 failed, nonzero matched. Native/Lua runtime interleave тест в T5 дополнителен и не заменяет эти assertions.

## T5 / M3: Lua adapter, схемы при подготовке и две error policies

**Create:** `Modules/Shared/Modding/Modding.Lua/LuaComponentDeclarations.cs`, `LuaQueryDeclarations.cs`, `LuaEcsContext.cs`, `LuaComponentDescriptors.cs`; `Modules/Shared/Modding/Modding.Core/ScriptSystemErrorPolicy.cs`, `ScriptSystemOptions.cs`; `tests/Karpik.Engine.Core.Runner.Tests/LuaEcsBindingsTests.cs`.

**Modify:** `LuaSystemDeclarations.cs`, `LuaScriptRuntime.cs`, `LuaSimulationPreparation.cs`, `Systems/LuaUpdateSystem.cs`; существующие `LuaSystemRegistrationTests.cs`, `LuaPreparationTests.cs`, `LuaEcsAllocationTests.cs`. Common host options в Modding.Core зарегистрировать Export+ServiceRegistration Simulation с intentional host override в installer; enum/options не ссылаются на MoonSharp.

**Consumes:** T1–T4 prepared common mechanisms. **Produces:** реальный Lua example из spec, inferred descriptors, host policy и owner cleanup.

- [x] Добавить failing `LuaEcsBindings_EndToEnd` в четырёх комбинациях Static/Dynamic × Deterministic/Parallel. Fixture: e1.Y=0, e2.Y=100, e3.Y=200/Temperature=99, e4 другой native component. Assertions: e1=20/e2=19/e3=99/e4 отсутствует; Positions прежние; второй capture пуст. Убедиться, что старый parser действительно даёт RED на queries.
- [x] Добавить failing tests parse/permission/lifetime: unknown ID во всех inc/exc/opt до frame 1, mixed duplicate field/type declarations, changed schema second Simulation, no extra VM execution, optional missing TYPE fails, readonly operations throw, foreign-owner saved pool/view access fails, disabled/re-enabled systems, disposed Simulation, break/restart. Мигрировать старые reads/writes fixtures на queries; сохранить отдельный migration-diagnostic тест и legacy on_update regression.
- [x] Реализовать parsing components рядом с systems, сбор полного набора схем из уже загруженных реальных VM, atomic freeze/compare перед bind/pipeline. Затем callback(dt,ctx) с кешированными args, handles/views/iterator values. Lua descriptors только dispatch к common typed operations; новые userdata/table на сущность не создавать, DynValue overhead измерять отдельно. No general CLR reflection descriptor на компонент.
- [x] Добавить f32 boundary tests: float.MaxValue, конечное double overflow, NaN, +Infinity/-Infinity, nil/string/bool. На невалидной записи прежнее поле сохраняется; если add уже выполнен, default компонент остаётся. Числовой entity ID проверять finite/integral/int-range/live до native call в Release.
- [x] Реализовать StopSimulation rethrow и LogAndContinue logging на declared callback boundary с unconditional finally gate cleanup, без изменения IsEnabled. Проверить ошибку после одного successful write и после add: partial values остаются; следующая система исполняется только в Continue; ошибочная callback снова вызывается в следующем кадре Continue. Startup error не проглатывается policy.
- [x] Доказать host path StopSimulation через настоящий ClientSimulationWorker/host fixture: fault доходит до ThrowIfFaulted, следующий frame не запускается. `GameplayLoopDriver.StepFrame` пропускает Late после исключения Update. Не объявлять обычный job-error log остановкой Simulation; если fixture обнаружит swallowing, исправить конкретный путь с отдельным regression test до GREEN. Для server/deterministic host проверить propagation аналогично в существующем runner fixture.
- [x] Перенести actual bindings в allocation matrix T0. Add-сценарий восстанавливает membership вне measured interval, иначе тест обманно измерит пустой запрос. Сравнить pure common, raw Lua, iterator, getter, setter, add и полный loop; проверить принятое B0/b ограничение и p99. При превышении сохранить цифры и остановиться на новом budget review, не удалять произвольную Lua-логику.
- [x] Запустить LuaEcsBindings + LuaSystemRegistration + LuaPreparation tests и Release LuaEcsAllocation. Записать свежие результаты в оба плана; исторические pass counts не копировать.

**Validation:** соответствующие Runner commands из ExecPlan; nonzero cases во всех четырёх комбинациях. Если временная test-only binding/probe из T0 больше не нужна, удалить её дублирование и оставить измерение production boundaries.

## T6 / M4: итоговая проверка и передача

**Modify:** spec, оба плана; `plans/script-modding-design.md`, `plans/script-modding-execplan.md`, `plans/lua-system-registration-remaining.md` только в части ссылок/статуса нового среза, не переписывать смежную работу. При завершении оформить `docs/02_ADR/` запись общих storage/lifetime/scheduling решений и принятого measured budget, имя по действующей конвенции каталога. Docs/knowledge learnings лишь предложить.

**Consumes:** результаты T0–T5. **Produces:** reviewable завершённый срез и честно записанные verification limits.

- [x] Запустить без filter Generator, Runner, ECS.Core, StaticAnalyzer test projects single-node; сначала relevant suites, без полного solution rebuild. Проверить `git diff --check` и scope changed files. Проверить source dependencies: MoonSharp только Lua adapter, нет object field transport и изменений third-parties.
- [x] Зафиксировать границы проверки: values, disable/re-enable, Simulation recreation/disposal проверены engine Runner fixtures. Ручная игра и NativeAOT publish не выполнялись; это явно указано в ExecPlan/ADR и не подменено Static compilation.
- [x] Обновить Progress/Discoveries/Decisions/Outcomes в ExecPlan и checkboxes здесь фактическими датами/командами/числами. Свести spec coverage с tests: ни один обязательный пункт не помечать завершённым только по чтению исходников.
- [x] Провести self-review API names/signatures, spec coverage, Release validation, ownership, resize/deletion recursion и migration. Публикация/PR запрещены текущим поручением; завершить передачей diff и результатов, не запускать finishing workflow с auto-push.

## Текущий результат и следующий шаг

T0–T6 завершены в границах экспериментального f32-среза. Actual slice: 1984+1464*N B/callback ≤4096+1600*N; common C# reserved operations 0 B. Fresh review/fixes завершены. Финальные полные suites: Runner Release266/266, Generator72/72, ECS.Core61/61, StaticAnalyzer54/54. Scoped Static/Dynamic DI2/2 входит в Generator. Ручная игра/NativeAOT не проверены; ограничения и один промежуточный cleanup file-lock сбой зафиксированы в ExecPlan. Текущая ветка, uncommitted, без push/PR.
