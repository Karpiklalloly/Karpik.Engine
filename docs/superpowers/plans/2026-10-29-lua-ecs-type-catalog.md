# Lua ECS scalar types Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Implementation and performance policy were approved by the subsequent «Делай»; task branch prerequisites still apply.

**Goal:** Добавить bool/i32/u32/f64 к f32 для native и dynamic ECS bindings с точными преобразованиями и сохранением lifetime guards.

**Architecture:** Существующий `IComponentField<TValue>` и typed SoA columns. Один sparse/dense membership на компонент, tagged field descriptor и explicit Lua kind dispatch. Генерация прямого CLR доступа без reflection/boxing.

**Tech Stack:** C#, текущий net10.0, Roslyn incremental codegen, Dragon ECS, MoonSharp, xUnit; новые зависимости отсутствуют.

**Spec:** [Каталог и контракт](../../../plans/lua-ecs-type-catalog-spec.md). **Overview:** [ExecPlan](../../../plans/lua-ecs-type-catalog-execplan.md).

## Global Constraints

- Реализация и performance policy согласованы; явное «в этой ветке делай» разрешает codex/modding-0.7 at baseline 8fae435 без новой ветки.
- Baseline inspection: `codex/modding-0.7` at `8fae435f561b022d5b74aaa74cafc910b1f0f37a`; новые main/prerequisites проверить перед исполнением.
- Existing F32=0; proposed Bool=1, I32=2, U32=3, F64=4. Dynamic tokens: `bool`, `i32`, `u32`, `f32`, `f64`.
- Общий C# reserved steady state — 0 B; Lua allocation policy новых workloads согласована. Старый f32 ceiling `4096 + 1600*N` не повышать.
- Не добавлять scalar boxing, reflection, hot-path locks/temporary arrays; managed `T[]` на колонку.
- Сохранять Shared boundaries, ownership, readonly, disposal, snapshot/membership/scheduler semantics и обе error policies.
- Не добавлять ctx:create, markers, nested/fixed/pointer/exact-number runtime, migrations, networking или editor changes.
- Все test/build команды ниже: cwd = законная task branch/worktree root, исходный root `C:/Users/artem/RiderProjects/KarpikEngine`. Single-node MSBuild `-m:1 -nr:false`.

## Review Focus

1. Невалидная integer запись не усекает дробь/не переполняется и сохраняет поле: T3.
2. Смена schema kind при том же имени не проходит canonical equality в живом ModSet: T1.
3. Typed view после swap/growth и вне owner/disposed scope не обходит защиту: T1, T4.
4. Новые CLR поля не приводят к скрытому пропуску unsupported/public readonly field и reflection fallback: T2.
5. Double проходит без float, а allocation budget сравнивает одинаковое число операций: T3, T4.

## Карта изменений

| Файлы относительно root | Изменение |
|---|---|
| `Modules/Shared/Modding/Modding.Core/Bindings/ComponentFieldDescriptor.cs` | Tagged metadata, пять typed constructor/getter путей |
| `.../Bindings/ComponentSchemaRegistry.cs` | Пять enum kinds, whitelist validation, equality kinds |
| `.../Bindings/PreparedComponentHandle.cs` | GetFieldKind и четыре пары typed view methods |
| `.../Bindings/DynamicF32ComponentPool.cs` → `DynamicComponentPool.cs` | Один mixed-kind pool с private typed columns; удалить старую реализацию |
| `.../Bindings/DynamicComponentStorage.cs` | Ссылки на новое имя пула; ownership/release порядок сохраняется |
| `src/Karpik.Engine.Core.Codegen/ClrComponentBindingGenerator.cs` | SpecialType mapping, typed emitted field methods, KEB001 |
| `Modules/Shared/Modding/Modding.Lua/LuaComponentDeclarations.cs` | Пять token→kind mappings и точные unknown-kind ошибки |
| `Modules/Shared/Modding/Modding.Lua/LuaEcsContext.cs` | ViewDescriptor.Index/SetIndex typed switch и строгие conversions |
| `tests/Karpik.Engine.Core.Runner.Tests/{DynamicComponentStorageTests,MixedComponentQueryTests,LuaEcsBindingsTests,LuaEcsAllocationTests,LuaSimulationLifecycleTests}.cs` | Invariants, Lua boundaries, runner matrix, allocation evidence |
| `tests/Karpik.Engine.Core.Generator.Tests/ClrComponentBindingGeneratorTests.cs` | Native typed regression, unsupported/readonly/discovery coverage |
| `tests/Karpik.Engine.Core.Runner.Tests/LuaBindingsTestScope.cs` | Менять только при необходимости повторного использования mixed-kind fixture |
| `Modules/Shared/Modding/Modding.Core/Bindings/{ClrComponentPool,IComponentField,IComponentPoolAccess}.cs` | Аудит callers/сигнатур; не менять автоматически |

`.../Bindings` выше означает `Modules/Shared/Modding/Modding.Core/Bindings`, а не иной проект. `LuaSimulationLifecycleTests.cs` содержит partial `LuaPreparationTests`: test filter должен учитывать имя метода, а не имя файла. `MixedComponentQueryTests`, F32 fixtures и generated provider imports сохраняются; test-only helpers не превращать в новый framework. Dependencies, project exports/installer registrations и module catalog не требуют изменений: все новые типы внутренние или используются существующим provider.

## T0: Утверждение и подготовка базы

**Files:** три документа пакета; при переносе см. destinations в ExecPlan.

- [x] (2026-10-29) Spec, policy новых allocation probes и runtime-реализация согласованы последующим сообщением «Делай».
- [x] (2026-10-29) HEAD 8fae435 и prerequisites проверены; пользователь «в этой ветке делай» явно разрешил продолжение в codex/modding-0.7, отдельную ветку не создавать.
- [x] (2026-10-29) Документы перенесены в plans/ и docs/superpowers/plans/, ссылки исправлены; оба плана отмечают начало T1.

## T1: Common typed API и mixed-kind SoA

Статус: завершено, Release 20/20 exit 0. Новые common regressions собраны в `tests/Karpik.Engine.Core.Runner.Tests/TypedComponentBindingsTests.cs`, существующие float fixtures сохранены. Initial missing-API compiler RED подтверждён; record formatting regression дала отдельный runtime RED→GREEN. Сигнатуры конструктора/named F32/getter/Deconstruct и record metadata cloning проверены.

**Files:** пять common change points из карты, rename pool; tests `DynamicComponentStorageTests.cs`, `MixedComponentQueryTests.cs`.

**Consumes:** `IComponentField<TValue>.Read(int) -> TValue`, `.Write(int,TValue) -> void`; прежний IComponentPoolAccess; нынешний ComponentCallbackScope.

**Produces:** enum/descriptor/view API из spec. Точные добавления к view: `SchemaScalarKind GetFieldKind(int field)`; `bool ReadBool(int field)`, `void WriteBool(int field,bool value)`; `int ReadI32(int field)`, `void WriteI32(int field,int value)`; `uint ReadU32(int field)`, `void WriteU32(int field,uint value)`; `double ReadF64(int field)`, `void WriteF64(int field,double value)`. Existing F32 methods остаются. DynamicComponentStorage.Resolve и публичный pool interface сохраняют сигнатуры.

T1 использует dynamic schemas для новых kinds; mixed native component до T2 не добавлять, иначе прежний generator закономерно даст KEB001 раньше common tests. Readonly field metadata можно проверить минимальным test accessor, а readonly handle/ownership — настоящим common query fixture. Handwritten provider не заменяет generated end-to-end проверку T2/T3.

- [x] Добавить RED tests `DynamicComponent_MixedKindsPreserveValuesAfterSwapGrowthAndReuse`: схема с именами B/I/U/F/D; значения true, int.MinValue, uint.MaxValue, 1.25f, 16777217d. Проверить корректное имя→поле после canonical sorting, swap-remove, forced growth, повторное add с false/0/0/0f/0d и release/reuse native entity ID. Сохранить keep-alive native marker там, где удаление последнего компонента иначе уничтожает entity.
- [x] Добавить `DynamicComponent_SchemaKindChangeRejectedWithoutReplacingFrozenCatalog`: один ID/version/name, сначала F32, затем I32 — отказ; старый каталог доступен и не изменён. Проверить также неизвестный `(SchemaScalarKind)999`, duplicate names и тот же набор kinds в ином порядке.
- [x] Добавить `MixedComponentQuery_TypedViewsEnforceKindRightsAndOwner`: wrong-kind accessor, readonly поле/handle, другая система, доступ вне callback, после disposal; на всех пяти kinds. Проверить совместимость F32 constructor/named F32 argument/getter и отказ null accessor.
- [x] Добавить `MixedComponentQuery_ReservedTypedAccessAllocatesZero`: подготовить/разогреть typed loops заранее; в измерении только capture/iterate/read/write с capacity; `GC.GetAllocatedBytesForCurrentThread()` delta == 0. Assertions/formatting вне измеряемой области, как в существующем allocation fixture.
- [x] Выполнить targeted команду ниже и сохранить RED причины. Только затем расширить typed descriptor и view, whitelist schema kinds и один private generic-column pool. Сохранить startup freeze/clone/equality, anchor/release/resize/dispose порядок. Не менять Int-ID generation semantics.
- [x] Повторить ту же команду, ожидается exit 0/все tests passed; записать фактический результат в ExecPlan и здесь, затем зафиксировать независимо проверяемый common milestone.

Run: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-restore -m:1 -nr:false --filter "FullyQualifiedName~DynamicComponentStorageTests|FullyQualifiedName~MixedComponentQueryTests"`.

## T2: Generated native fields

Статус: завершено. Initial regression отказала KEB001 на bool; полный Generator Release 76/76 exit 0. Существующие RS2008 release-tracking warnings остаются вне scope.

**Files:** actual Codegen generator и `ClrComponentBindingGeneratorTests.cs`.

**Consumes:** T1 descriptor overloads для bool/int/uint/float/double; existing ClrComponentPool<T>.ReadComponent/MutableComponent.

**Produces:** existing generated provider/factory API с typed field implementations; public registration signatures не меняются.

- [x] Добавить RED test `ClrComponentBinding_GeneratesTypedScalarAccess`: component `bool B; int I; uint U; float F; double D;` и readonly counterparts. Emit/load assembly через existing GeneratorTestHarness; проверить реальные read/write значений из T1, исходный struct изменён через mutable ref, readonly descriptor.CanWrite=false и запись отвергается. Проверить double 16777217d.
- [x] Обновить существующий unsupported test: `int` теперь supported, `long` всё ещё даёт KEB001. Добавить bool/int/uint/double в supported matrix, fixed buffer/nested/enum — в unsupported matrix; неподдержанное поле не исчезает молча из provider.
- [x] Run Generator suite (ниже), сохранить RED; расширить только SpecialType mapping/typed emission в Execute. Не менять Registrations, discovery и provider naming без обнаруженной необходимости.
- [x] Повторить suite; существующие referenced assembly, own Static Client/Server, Dynamic composition tests должны пройти. Зафиксировать фактический GREEN и commit milestone.

Run: `dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -c Release --no-restore -m:1 -nr:false`.

## T3: Lua schema declarations и строгие setters

Статус: завершено. После исправления raw-string delimiter в новом test fixture наблюдался runtime RED «only f32» на bool field. Semantic suite 87/87 и mixed lifecycle real Runner 4/4 = 91/91 Release exit 0. Команда Runner: `--filter "(FullyQualifiedName~LuaTypedBindingsTests&FullyQualifiedName!~LuaEcsAllocation)|FullyQualifiedName~DeclaredLuaSystems_RecreateScopes_DisposeOldAccess"` с общими flags из Global Constraints. Новые tests находятся в LuaTypedBindingsTests.cs; lifecycle fixture расширен до пяти kinds, 24 disposal guards и 51 WeakReference на case.

**Files:** `LuaComponentDeclarations.cs`, `LuaEcsContext.cs`, `LuaEcsBindingsTests.cs`, по необходимости существующие Lua preparation fixtures.

**Consumes:** T1 typed methods/kinds и T2 generated providers.

**Produces:** пять точных Lua tokens и typed getters/setters; прежний Lua query shape, policy и entry points сохраняются.

- [x] Добавить RED `LuaEcsBindings_TypedScalarsRoundTripNativeAndDynamic`: настоящий Lua callback получает native mixed component, записывает dynamic mixed component, затем другой callback пишет обратно в mutable native. Assert bool Lua type=boolean, numeric type=number и точные CLR значения. Подготовить все регистрации через existing codegen, не handwritten provider для этого end-to-end test.
- [x] Добавить setter matrix: bool accepts true/false и отвергает 0/1/nil/строку/table; i32 accepts −2147483648/−1/0/2147483647, rejects −2147483649/2147483648/2147483647.5/0.5/NaN/±Infinity/строку/bool; u32 accepts 0/−0/1/4294967295, rejects −1/4294967296/4294967295.5/0.5/NaN/±Infinity/строку/bool. Для каждого отказа до записи установить sentinel и проверить неизменность native и dynamic поля.
- [x] Проверить f64 значения 16777217, `1 + 2^-40`, double.MaxValue, ±0, NaN и ±Infinity без float промежуточного значения; неправильные Lua types отвергаются. Прежние f32 nonfinite/finite overflow tests сохраняются; добавить отрицательный ноль с проверкой знакового бита вне Lua.
- [x] Добавить startup tests для mixed schema tokens, unknown/case-mismatched token, field-kind conflict между entry points и повторной Simulation в том же ModSet. Assert отказ до callback, атомарность frozen catalog.
- [x] Добавить сценарий «успешная запись → неверная integer запись» для обеих policies: ранее записанное и add сохраняются, неверное поле сохраняет sentinel, gate освобождается; StopSimulation останавливает следующие системы, LogAndContinue запускает их и повторяет callback на следующем кадре. Ошибка содержит component, field, expected kind и owner/query context.
- [x] Run targeted suite (ниже), сохранить RED; добавить пять token mappings и kind switch в существующий ViewDescriptor. Перед checked integer cast проверить DataType, finite, integral, inclusive range; для bool не использовать truthiness/ToObject.
- [x] Повторить targeted suite и зафиксировать GREEN. Запустить end-to-end mixed сценарий через настоящий Runner в матрице Static/Dynamic × Deterministic/Parallel с переиспользованием existing fixtures; отдельно утверждать только реально выполненные cases.

Run: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-restore -m:1 -nr:false --filter "FullyQualifiedName~LuaEcsBindings|FullyQualifiedName~LuaPreparationTests"`.

## T4: Performance, lifecycle и закрытие среза

Статус: завершено локально. Финальный Runner Release 390/390 и Generator 76/76 exit 0 на восстановленном fixture. [Измерения](../../../plans/lua-ecs-type-catalog-evidence.md) сохранены. F32 init/with совместимы, negative-control 4/4 failed при пропущенном Dispose. Независимое review 8fae435..65a8c08 без Critical/Important/Minor findings.

**Files:** существующие allocation/lifecycle tests и docs из карты; production только при воспроизведённой проблеме в этом scope.

**Consumes:** T1–T3; утверждённая policy измерений.

**Produces:** воспроизводимое evidence по всем пяти kinds; f32 совместимость; обновлённые spec/ADR и фактические outcomes.

- [x] Оставить existing `LuaEcsAllocation_ActualSliceStaysWithinApprovedExperimentalBudget` без повышения `4096 + 1600*N`. Добавить четыре однородных фактических Runner/Lua workload вида native get/read→dynamic add/write для bool/i32/u32/f64; N=0/1/16/256/4096, Release, warmup=2000, measured callbacks>=1000. Membership reset до таймера; formatting/logs после. Проверять результат, иначе пустая query даст ложный PASS.
- [x] Для mixed-five workload на каждую entity выполнить один native get, один dynamic add и пять field read/write пар; f32 baseline использует один native и один dynamic компонент с пятью float-полями и те же операции. Пять отдельных get/add пар — несопоставимый baseline. Performance fixture использует значения, допустимые в обоих представлениях. Для каждого N=0/1/16/256/4096 assert `B/callback(mixed-five) <= B/callback(five-f32 baseline)`. Записать B/callback, B/entity, p50/p95/p99 и Gen0; forced growth — отдельная серия. Новую абсолютную latency-гарантию не придумывать. При превышении сохранить evidence и остановить milestone до решения разработчика, не расширять waiver.
- [x] Обобщить `DeclaredLuaSystems_RecreateScopes_DisposeOldAccess_AndReleaseReferences` на mixed schema/новые typed views, сохранив три Setup/Destroy и четыре composition/scheduler cases. Assert fresh values, invalid old access, отсутствие старых callbacks и collectability новых descriptor/column/view объектов. Сохранить возможность negative control fixture без Dispose. Это не доказывает сохранение state при editor reload.
- [x] Выполнить полные Generator и Runner команды из T1/T2 без filter; обе должны закончиться exit 0. ECS.Core/StaticAnalyzer отдельно запускать, если diff затронул их контракты/diagnostics; при текущем scope такие изменения не планируются. Проверить `git diff --ignore-submodules --check` и список changed files.
- [x] В task branch обновить существующие `plans/lua-ecs-bindings-spec.md`, `plans/lua-ecs-bindings-execplan.md`, `plans/script-modding-execplan.md`, `docs/02_ADR/lua-ecs-common-bindings.md` только по фактически принятому/реализованному scope и evidence. Актуализировать detailed/overview пару; failed/unexecuted checks оставить явными. Обновить QMD index изменённых repository docs, если коллекция доступна.
- [x] Итоговое review: source compatibility F32 constructor/getter/Deconstruct/init/with, все пять Review Focus случаев и connected lifetime/GC paths проверены независимым reviewer; actionable findings отсутствуют. Runtime после финальных tests/review не менялся.
- [x] Публикация исключена действующим ограничением базового Lua/ECS-плана: без push/PR, работа сохраняется локальными commits в явно разрешённой codex/modding-0.7.

## Результаты подготовки этого плана

Сначала подготовлены документы и выполнен builder-review; он уточнил pass/fail mixed-five allocations до `mixed-five <= five-f32 baseline`. Затем отдельные «Делай» и «в этой ветке делай» разрешили реализацию в codex/modding-0.7. T0–T4 завершены; полный Runner 390/390 и Generator 76/76 exit 0, все allocation gates пройдены, final code review без findings. Проверки и измерения находятся в [ExecPlan](../../../plans/lua-ecs-type-catalog-execplan.md) и [evidence](../../../plans/lua-ecs-type-catalog-evidence.md). Manual game/NativeAOT/editor reload не выполнялись; публикация исключена. Repository files сохранены локальными commits.
