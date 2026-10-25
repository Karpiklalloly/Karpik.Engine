# Lua ECS bindings: проверенные возможности и открытые проверки

Дата проверки: 2026-10-25, Europe/Samara. Repository: `C:\Users\artem\RiderProjects\KarpikEngine`. [Спецификация](lua-ecs-bindings-spec.md), [ExecPlan](lua-ecs-bindings-execplan.md).

## Метод и ограничения исходного аудита (до реализации)

Полностью прочитаны передача `C:\Users\artem\.codex\visualizations\2026\10\24\01a16e7b-3b60-7291-b519-65039d617fcf\lua-ecs-bindings-handoff.md`, корневой AGENTS.md и plans/PLANS.md. Прочитаны профильные skills из `skills/karpik-engine-architecture`, `skills/karpik-dragon-ecs`, `skills/karpik-engine-performance`, `skills/karpik-testing`, инструкции третьестороннего DragonECS и skills планирования/сверки документации. Каталоги профильных skills находятся в `skills/`, не `.agents/skills/`; первый lookup по неверному пути был исправлен.

Проверка структурных инструментов: отдельного доступного semantic/code graph tool для checkout не найдено; `.understand-anything` и `.qmd` в корне отсутствуют. Выполнен один QMD query по концепциям ModSet/Simulation/bindings/scheduler: полезного документа он в ограниченное время не вернул, вывод остановился на Expanding query. Использованы известные из передачи пути, актуальные symbols и focused rg; QMD не применялся как поиск кода. Canonical DI/scope doc и соответствующие legacy modding documents просмотрены; их прежние reads/writes, batches и error policy не заменяют поздний контракт передачи.

MEMORY использована только как указатель на прежние allocation-проверки и расположение исходников. Исторические цифры не объявляются свежими. В этой работе не выполнялись dotnet build/test, ручная игра, NativeAOT publish, новые generators/bindings или тесты будущего API.

## Git и PR

`git status --short --branch --ignore-submodules`: чисто, `codex/modding-0.7...origin/codex/modding-0.7 [ahead 7]`.

`git rev-parse HEAD`: `50b1d7586c92c1177a836d7a35e601736e2b4f6b`.

Origin URL: `https://github.com/Karpiklalloly/Karpik.Engine`. Fetch не выполнялся; ahead относится к локальному tracking ref.

GitHub GET PR #101: state=open, draft=true, title=`fix: Restore cooked asset loading during modding refactor`, head=`codex/modding-0.7`, remote SHA=`74353a4d7a3799995001975881b0ce4b01ee3711`, base=`main`. [PR #101](https://github.com/Karpiklalloly/Karpik.Engine/pull/101). Никаких мутаций PR не выполнялось.

## Матрица неизвестных передачи

| Вопрос | Подтверждено исходниками | Выбранное инженерное предложение | Что ещё нужно доказать |
| --- | --- | --- | --- |
| Общие registry/storage/query и DI | ECS.Core уже доступен Modding.Core; Modding.Lua зависит от Modding. Engine/ModSet/Simulation scopes существуют | Common bindings в ECS.Core; Engine CLR metadata, ModSet schemas, Simulation data; Lua только adapter | Static/Dynamic разрешение всех новых scope exports, отсутствие утечки Simulation в Engine |
| CLR field codegen и подключаемые сборки | Generator подключён к Modules; RuntimeCompositionGenerator собирает services из referenced symbols; dynamic discovery использует reflection при startup | Семантически собирать закрытые Register<T>; generated provider; явная модель provider для own static host, compiled exports для referenced DLL | Compile/execute multi-assembly fixture, analyzer подключение вне Modules, generic forwarding diagnostics, cache invalidation |
| Каталог полей и conversions | Текущий registry хранит только name→Type; поля ещё не экспортируются | В первом срезе float X/Y и f32 Value, typed ref access; unsupported public fields диагностируются | Фактическая генерация/precision tests; полный каталог char/fixed buffers/точных чисел позднее |
| MoonSharp costs | Локальный probe ниже подтверждает DynValue class и ненулевую стоимость raw Lua numbers/arithmetic | Измерять отдельные границы и общий цикл; 0 B у common C# после reserve, отдельный согласованный VM budget | Field getters/setters, reusable view return, iterator, точные числа и p99 ещё не измерены |
| Независимые buffers и рост | Native EcsSpan содержит int; pool.ToSpan инвалидируется структурными операциями; queries используют cached executor buffers | Materialize named snapshot до Lua; отдельный int[] и курсор; OnWorldResize сохраняет prefix; iterator держит query | Вложенные queries, resize активного snapshot, break/error/restart, отсутствие новых объектов на сущность |
| Cleanup/anchor/reuse | OnReleaseDelEntityBuffer вызывается после native pools и до dispenser.Release; OnDelEntity помечает сущность мёртвой раньше; public resize/destroy listeners есть | Common storage cleanup в release hook без native Del; normal last dynamic Del снимает anchor | Реальные tests порядка native deletion, partial release, last component, destroy/dispose и повторного ID |
| Scheduler dynamic identity | Descriptor и BuildComponentTypes принимают Type; graph уже использует int bitsets; sequential сериализует со всеми | Discriminated CLR/schema access identity; нормализация на startup; derived anchor write | Обновление обоих Build путей и compatibility metadata, synthetic non-sequential graph tests |
| Документы/ветка | Текущая ветка относится к cooked assets PR; task-ветка от task-ветки требует согласия | Внешний пакет документов; перенос только после решения о допустимой базе | Ветка не выбрана; черновик не внесён в git |

Это source feasibility audit, не доказательство работоспособности будущих bindings. Возможность использовать публичные hooks подтверждена; корректность нового обработчика остаётся будущей тестовой обязанностью.

## Конкретные source anchors

Все пути ниже относительно repository, line numbers относятся к проверенному HEAD; third-party checkout мог иметь собственную revision, его source не менялся.

* `Modules/Shared/Modding/Modding.Lua/LuaComponentRegistry.cs:10`: Engine registry, Register<T>, name→Type, Seal, отсутствие field access.
* `Modules/Shared/Modding/Modding.Lua/LuaSystemDeclarations.cs`: список допустимых system keys пока содержит reads/writes, queries не реализованы.
* `Modules/Shared/Modding/Modding.Lua/LuaSimulationPreparation.cs`: lifecycle.Load → components.Seal → AddInstance до pipeline.
* `Modules/Shared/Modding/Modding.Lua/LuaScriptRuntime.cs:108-144`: InvokeSystem, non-reentrant gate, NewNumber(dt), Script.Call, finally cleanup.
* `Modules/Shared/Modding/Modding.Lua/Systems/LuaUpdateSystem.cs`: RuntimeSystem + SequentialSystem, TryStartUpdate, Time.DeltaTime.
* `Modules/Shared/Modding/Modding.Core/ModContainer.cs:56-85`: enabled/start gate, Destroy→Unload→Dispose. Готовой host error policy здесь нет.
* `Modules/Shared/ECS/ECS.Core/EcsModuleInstaller.cs:11-26`: native worlds Simulation lifetime, world.Destroy при release.
* `Modules/Shared/ECS/ECS.Core/WorldEventListener.cs`: пример подключения entity listener; он не заменяет world release-listener.
* `third-parties/DragonECS/src/EcsWorld.cs:734-751`: DelEntity меняет isUsed и уведомляет entity listeners; не возвращает ID dispenser сразу.
* `third-parties/DragonECS/src/EcsWorld.cs:797-808`: пустые сущности проходят штатное deferred deletion.
* `third-parties/DragonECS/src/EcsWorld.cs:1441-1512`: release empty buffer → native pool cleanup → groups → world listener (1498) → dispenser.Release (1502).
* `third-parties/DragonECS/src/EcsWorld.cs:1544-1568,2026-2036`: resize arrays, pool hooks, world resize listener; интерфейсы world/entity lifecycle.
* `third-parties/DragonECS/src/Pools/EcsPool.cs:159,206-248,594-600`: начальная dense capacity по config, автоматический рост, sparse resize отдельно.
* `third-parties/DragonECS/src/Pools/EcsPool.cs:311-344`: Has без liveness validation; часть Add/Get/Del checks только в DEBUG/stability mode.
* `third-parties/DragonECS/src/Pools/EcsPool.cs:695-705`: ToSpan с native int-источником и Densify; нельзя хранить span после add/del.
* `third-parties/DragonECS/src/Collections/EcsSpan.cs:28-31,94-97`: ref struct с ReadOnlySpan<int>, публичный int indexer.
* `third-parties/DragonECS/src/Executors/Queries.cs:18-19,60-64`: buffers принадлежат cached executor, повторная query может перезаписать результат.
* `src/Karpik.Engine.Core/Scheduling/EcsComponentAccessDescriptor.cs:5-12`: только Type identity.
* `src/Karpik.Engine.Core/Scheduling/EcsUpdateGraphBuilder.cs:26-32,82-89,92-150,254-272`: оба пути графа, type normalization, Write dominates Read, sequential/global conflicts.
* `src/Karpik.Engine.Core/Scheduling/EcsUpdateGraph.cs:23-26`: ComponentTypes — публичная CLR-only metadata; её нельзя молча превратить в mixed keys.
* `Directory.Build.props:32-44`: analyzer/codegen подключается проектам под Modules. Это не гарантия автоматического подключения для внешнего SDK-проекта.
* `src/Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/RuntimeCompositionGenerator.cs:116-152,164-188,653-731`: own source model, Shared/Dynamic ранний выход, referenced exports для Static.
* `src/Karpik.Engine.Core.Runner/DynamicCompositionDiscovery.cs:7-13,75-89`: startup reflection ограничена Dynamic path; не тянуть этот механизм в Static.
* `src/Karpik.Engine.Core/Scheduling/EcsUpdateScheduler.cs:199-203,313-329`: sequential callback вызывается непосредственно, исключение здесь не проглатывается; нужно проверить host propagation, а не только adapter throw.
* `src/Karpik.Engine.Core/ClientSimulationWorker.cs:71-77,134-143` и `src/Karpik.Engine.Core.Runner/WorkerHost.cs:487-493`: worker сохраняет frame exception, host вызывает ThrowIfFaulted до следующего запроса. Это подходящий существующий путь для проверки StopSimulation.
* `tests/Karpik.Engine.Core.Runner.Tests/LuaSystemRegistrationTests.cs:41-83`: существующий allocation regression test сравнивает direct/runtime/adapter; не доказывает bindings и не запускался здесь.

## Свежий allocation probe

Источник: [lua-ecs-moonsharp-probe.ps1](C:/Users/artem/.codex/visualizations/2026/10/24/01a16f92-f417-7d41-a59f-8b29a6ff771a/lua-ecs-moonsharp-probe.ps1). Выполнен успешно отдельным PowerShell process, локальная Release DLL `Modules/Shared/Modding/Modding.Lua/bin/Release/net10.0/MoonSharp.Interpreter.dll`. Эта DLL не пересобиралась и не подтверждает соответствие остальных compiled artifacts HEAD. Измерялся сам MoonSharp, не код проекта.

SHA256 DLL: `d1caf8c1a1cd7669b749d4cacdda88e949d5138b86e8ac3f73943abebd6e7e18`. Повторный запуск в новом PowerShell process вернул те же пять значений B/callback и 48 B/value, exit 0.

Команда из repository root:

```powershell
& 'C:\Users\artem\.codex\visualizations\2026\10\24\01a16f92-f417-7d41-a59f-8b29a6ff771a\lua-ecs-moonsharp-probe.ps1'
```

Скомпилированный in-memory C# вызывает Script.Call напрямую, чтобы не включать PowerShell binder в интервал. Таблица ctx и массив аргументов подготовлены заранее; NewNumber(dt) включён в измерение. На вариант 2000 warmup и 1000 measured calls. Генерация callback и вывод исключены из интервала. Lua-body: `local x=0; for i=1,N do x=x+i*0.01 end`.

```text
MoonSharp=2.0.0.0; CLR=10.0.11; DynValueIsValueType=False
N=0      752 B/callback
N=1      992 B/callback
N=16    4592 B/callback
N=256  62192 B/callback
N=4096 983792 B/callback
NewNumber=48 B/value
```

Разности здесь точно 240 B/итерацию; это наблюдение для указанного body, а не формула для произвольного Lua. Число 752 относится к callback с двумя аргументами и локальным циклом, не к прежнему empty callback(dt). Probe не измеряет field access, bindings iterator, exact arithmetic, p99 и рост ECS storage. Не вычитать эти результаты как универсальный baseline из любого будущего сценария: сравнивать структурно сопоставимые callbacks.

Первая попытка in-memory compile завершилась CS1701 из-за ссылки MoonSharp на System.Runtime 4.1 при host .NET 10. Повторная успешная попытка подавляет только предупреждение 1701; другие compilation errors остаются ошибками. Тестовые и production assemblies не патчились.

## Необязательные learnings для будущей docs/knowledge

Предложения, автоматически не записаны в repository: cached Dragon queries не являются устойчивыми snapshots; world release-listener стоит перед повторным использованием ID; Release bindings обязаны валидировать native IDs сами; sequential graph tests скрывают пропущенные accesses; raw Lua arithmetic имеет линейную allocation cost даже без userdata. Записывать эти заметки в docs/knowledge только после подтверждения разработчика.

## Проверка пакета документов

Выполнена локальная проверка относительных Markdown links и парности code fences всех четырёх документов: 0 ошибок. Все 13 обязательных разделов ExecPlan присутствуют. Self-review сверил контракт f32-среза с T0–T6, типы bound fields/factory и размещение error options приведены к одному варианту. Последний git status остаётся clean на 50b1d75/ahead 7. Эти проверки относятся к документам, не к будущему production-коду.

## Реализация и свежие замеры, 2026-10-25

Позднее пользователь поручил реализацию, разрешил текущую ветку и экспериментальный
ceiling `4096 + 1600*N` bytes/callback после reserve. Поэтому исходные отметки выше
«код не менялся / tests не запускались / clean tree» относятся только к аудиту.
Рабочие изменения теперь есть; push/PR/коммиты не выполнялись.

Версионируемые probes: [LuaEcsAllocationTests](../tests/Karpik.Engine.Core.Runner.Tests/LuaEcsAllocationTests.cs)
и [реальный ECS-сценарий](../tests/Karpik.Engine.Core.Runner.Tests/LuaEcsBindingsTests.cs).
По 2000 warmup и 1000 measured calls, reset membership вне интервала.
Общие C# capture/pool/field operations дали 0 managed bytes за 1000 calls после reserve.

| N | Actual ECS bytes/callback | p50 us | p95 us | p99 us |
| ---: | ---: | ---: | ---: | ---: |
| 0 | 1984 | 1.2 | 4.6 | 6.5 |
| 1 | 3448 | 2.1 | 4.1 | 6.0 |
| 16 | 25408 | 15.9 | 23.7 | 343.4 |
| 256 | 376768 | 341.9 | 1456.8 | 2165.5 |
| 4096 | 5998528 | 5771.2 | 7322.2 | 8106.3 |

Это первый actual Release run .NET 10.0.13 / MoonSharp 2.0.0.0. Наблюдаемая
allocation-модель точно `1984+1464*N`; локальные latency percentiles не гарантируются.
T0 raw-boundary profile имел другую Lua-body и `1384+1560*N`, поэтому его нельзя
подставлять вместо actual result. Generic DynValue wrappers могут копироваться,
но host view переиспользуется и пишет прямо в native/dynamic pools.

Отдельный numeric probe подтвердил: `math.huge=double.MaxValue` (finite),
`1/0=+Infinity`, `-1/0=-Infinity`, `0/0=NaN`. Setter правильно отклоняет конечный
overflow и принимает уже нечисловые/бесконечные float values по контракту.

Fresh review проверил lifecycle, schema freeze, buffer growth, ownership,
генератор/discovery и key compatibility. Авторская default-descriptor regression
выявлена полным Runner (260/265), затем закреплена RED→GREEN; диагностическая
ошибка invalid dynamic mode исправлена вместе с ней. Query/component context
добавлен на error path; отдельный тест RED→GREEN. Scoped Static/Dynamic constructor
resolution 2/2 проверяет реальные factories/attributes, не ручное new preparation.
Итоговые suite counts фиксируются в ExecPlan после полного повторного прогона.

Финальный результат: Runner Release266/266, Generator72/72, ECS.Core61/61,
StaticAnalyzer54/54; exit 0. Одно предыдущее падение ProcessManager lifecycle
при очистке temporary bundle (Windows file lock) прошло без правок ProcessManager:
отдельный repeat 2/2, затем полный Runner266/266. Старые пять scheduler regression
failures исправлены и закрыты regression tests. `git diff --ignore-submodules --check`
успешен; без ignore-submodules Git пытался читать защищённый submodule metadata,
поэтому этот незавершённый check не выдавался за clean. Submodules не менялись.

Ручная игра и NativeAOT publish не проверялись. Исходные более широкие типы,
markers, ctx:create и schema migration не выдаются за реализованные.
