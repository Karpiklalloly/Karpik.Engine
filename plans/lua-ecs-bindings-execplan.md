# Провести Position → mixed query → Lua → Temperature

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

Дата: 2026-10-25, Europe/Samara. Реализация поручена сообщением «Делай»; затем пользователь явно указал «В текущей ветке делай». [Спецификация](lua-ecs-bindings-spec.md), [подробный план](../docs/superpowers/plans/2026-10-25-lua-ecs-bindings.md), [source/probe evidence](lua-ecs-bindings-evidence.md).

## Текущее состояние

- 2026-10-29: реализовано расширение scalar kinds bool/i32/u32/f64. Актуальный ход приёмки — [typed ExecPlan](lua-ecs-type-catalog-execplan.md), конкретные allocation workloads — [evidence](lua-ecs-type-catalog-evidence.md). Этот документ сохраняет milestones первого f32-среза и историю внешнего sandbox; scope изоляции и запрет push/PR продолжают действовать.

- Статус: завершён экспериментальный f32-срез и автоматическая приёмка.
- Сейчас: первый срез проверен внешним SDK-проектом LuaEcsSandbox; lifecycle-регрессии перенесены в Runner.Tests, финальный полный прогон описан в Progress.
- Далее: следующее явное поручение; stop/start через backend редактора и IPC проверен, hot reload/state restore отдельно не проверен. Без push/PR.
- Блокеры: нет. Разрешён экспериментальный бюджет 4096 B/callback + 1600 B/entity после reserve; common C# слой — 0 B. Работа в текущей ветке явно разрешена; публикация запрещена.

## Purpose / Big Picture

Обеспечить первый законченный путь: CLR Position с generated f32 fields → common query с inc readonly Position и exc mutable Temperature → обычный Lua for по int-ID → common dynamic Temperature.Value. Общий слой не зависит от VM; runtime, данные, handles и lifecycle изолированы по Simulation; схема общая для ModSet. После выполнения Lua новые Temperature видны CLR и другим модам, а повторная query больше не выбирает эти сущности.

Это план ограниченного среза, не реализация всего согласованного type catalog. Не вводить runtime-specific storage, object/boxing, entity wrappers, обязательные batch loops, reflection field access, schema hot reload или сетевую сериализацию. Не расширять cooked assets PR #101. Ничего не публиковать без отдельного поручения.

## Progress

- [x] (2026-10-25) Полностью прочитаны handoff, AGENTS.md, plans/PLANS.md; профильные skills и текущие источники сверены.
- [x] (2026-10-25) Ветка codex/modding-0.7, HEAD 50b1d75, ahead 7, clean tree подтверждены; PR #101 открыт/draft/cooked assets подтверждён GET.
- [x] (2026-10-25) Проверены public native candidate/lifecycle APIs, Type-only scheduler и DI/codegen change points; оформлены границы доказанного.
- [x] (2026-10-25) Выполнен внешний raw MoonSharp probe: NewNumber 48 B/value; конкретный цикл 752+240*N B/callback. Это не bindings benchmark.
- [x] (2026-10-25) Спецификация, этот обзор и подробные задачи подготовлены как связанные внешние документы.
- [x] (2026-10-25) Получено поручение реализации и явное исключение из branch-правила: работать в текущей codex/modding-0.7. Main de48fc0 проверен локально и на GitHub, нужные Lua prerequisites в него ещё не вошли. Документы перенесены в repository, без push/PR.
- [x] (2026-10-25) M0 / T0: текущая ветка и реализация явно разрешены; Release boundary probes 6/6, exit 0. FullSliceBoundary=1384+1560*N B/callback; N=4096 p99=6919.4 us. Пользователь разрешил экспериментальный потолок 4096+1600*N B/callback, common C# 0 B. Growth измерен отдельно; production bindings этим не доказаны.
- [x] (2026-10-25) Промежуточное прерывание T1 восстановлено: затем полный Generator70/70 и LuaPreparation32/32 прошли; окончательные результаты с новыми regression tests ниже.
- [x] (2026-10-25) M1 / T1–T2: полный Generator 70/70, LuaPreparation 32/32; Release DynamicComponent 7/7. Команды dotnet test соответствующих test projects с --no-restore -m:1 -nr:false; storage — -c Release --filter FullyQualifiedName~DynamicComponent. Native library не менялась.
- [x] (2026-10-25) M2 / T3–T4: mixed query tests 7/7, C# reserved steady state 0 B; core scheduling tests первоначально 56/56. Review/regressions добавили проверки default sentinel, invalid mode и empty key; финальный полный прогон ниже.
- [x] (2026-10-25) M3 / T5: Release Lua Ecs/budget 25/25, реальный Runner Static/Dynamic × Deterministic/Parallel 4/4, Worker Stop/Continue × scheduler modes 4/4. Actual ECS allocation: 1984+1464*N B/callback, ниже разрешённого 4096+1600*N.
- [x] (2026-10-25) M4 / T6: полный Runner Release266/266, Generator72/72, ECS.Core61/61, StaticAnalyzer54/54, все exit 0. Fresh review и fixes завершены; Markdown links/fences и git diff --ignore-submodules --check чистые. Ручная игра/NativeAOT явно не проверены.
- [x] (2026-10-25) По отдельному поручению пользователя 15 binding-файлов перенесены в Modding.Core/Bindings, namespace Karpik.Engine.Shared.Modding.Bindings. Обновлены Lua, generators, compiler metadata references, тесты и документы; новые project references не добавлены. После переноса свежие Generator72/72 и Runner Release266/266, exit 0. Первые запуски через agent shell прерваны до тестов: VSTest/testhost connection timeout 90 s; повтор через context-mode с VSTEST_CONNECTION_TIMEOUT=180 и --no-build --no-restore прошёл на свежесобранных сборках. ECS.Core/StaticAnalyzer suites после переноса отдельно не повторялись; предыдущие результаты выше исторические.

- [x] (2026-10-28) Lifecycle-регрессии внешнего sandbox перенесены в Runner.Tests с переиспользованием существующих fixtures. Один Runner проходит три Setup/Destroy в каждой из четырёх комбинаций Static/Dynamic × Deterministic/Parallel. Проверяются fresh data, exact lifecycle, disable/re-enable, остановка старых adapters/callbacks, 15 disposal guards и сборка 21 старого объекта на case через WeakReference. Initial targeted 4/4, negative control без Dispose preparer в test fixture дал 4/4 failed; fixture восстановлен. Финальный полный Runner Release270/270, exit 0: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-restore -m:1 -nr:false`. Изменены только тесты/fixture и этот план; production, editor/IPC и ImGui не менялись. Push/PR/commit не выполнялись.

## Surprises & Discoveries

- Observation: ImGui по умолчанию сохранял `imgui.ini` в cwd immutable bundle, блокируя SDK rebuild и editor restart. Общий ImGuiRenderContext теперь задаёт абсолютный путь под LocalApplicationData/Karpik/ImGui с ключом runtime working directory. UTF-8 filename живёт до финального save при DestroyContext; NeoVeldrid освобождает device resources, native context освобождает владелец. Нет новых операций в Update/Render.
  Evidence: native CPU regressions сначала 2/2 failed, затем полный Graphics.Core.Tests 42/42; Graphics.Core Release build exit 0. Локальный SDK `0.6.0-local-20261028-imgui-967a0f0` установлен для внешнего sandbox, Debug-клиент пересобран. Два полных Debug start/stop через настоящий EditorPreviewController: Ready, Lua PASS, IPC snapshot=4, unload, внешний ini save и повторная ValidateStatic — exit 0. Кнопки UI не автоматизированы: computer-use app approval timed out. Существовавшие изменения обоих Generated catalog files сохранены побайтно; commit/push/PR не выполнялись.
- Observation: внешний LuaEcsSandbox проверил шесть SetupAsync/Destroy в одном процессе; эти API пересоздают всю цепочку Engine/ModSet/Simulation, а не только Simulation внутри живого ModSet. Это не проверка editor/IPC hot reload.
  Evidence: внешний `D:/Games/LuaEcsSandbox/.logs/lifecycle-check.json`, 2026-10-27; независимый SDK-стенд сохранён вне engine repository.
- Observation: regression `DeclaredLuaSystems_RecreateScopes_DisposeOldAccess_AndReleaseReferences` переиспользует fixtures движка и один Runner на три Setup/Destroy. Матрица Static/Dynamic × Deterministic/Parallel; exact lifecycle, disable/re-enable, fresh values после poison=99, 15 disposal guards и 21 weak reference на case. GC выполняется после выхода из NoInlining helper и завершения JobSystem. Внутренний MoonSharp.Script отдельно не наблюдается.
  Evidence: `tests/Karpik.Engine.Core.Runner.Tests/LuaSimulationLifecycleTests.cs`. Initial targeted 4/4; negative control без query preparer Dispose в test fixture — 4/4 failed, fixture восстановлен. Production-код не менялся.
- Observation: scheduler graph already has numeric access arrays, но подготовка descriptor принимает только Type. Dynamic identity надо расширить в обоих Build путях, не переносить строки в кадр.
  Evidence: `src/Karpik.Engine.Core/Scheduling/EcsUpdateGraphBuilder.cs:26-32,82-150`.
- Observation: sequential система конфликтует со всеми, а не только со своей VM; тест последовательности Lua не доказывает dynamic dependency inference.
  Evidence: тот же файл, `AddAccessDependencies`, строки 254–272.
- Observation: `OnReleaseDelEntityBuffer` после native pools и до dispenser.Release позволяет очистить common storage без патча DragonECS и без рекурсивного anchor.Del.
  Evidence: `third-parties/DragonECS/src/EcsWorld.cs:1474-1504`. Это source-порядок, ещё не тест нового storage.
- Observation: pool Has и многие native checks не защищают Release trust boundary; cached native query buffer не имеет нужной snapshot lifetime.
  Evidence: `Pools/EcsPool.cs:206-248,311-344,695-705`; `Executors/Queries.cs:18-19`.
- Observation: fresh raw VM probe имеет ненулевую линейную стоимость даже без userdata. First attempt CS1701 исправлена подавлением только этой assembly identity warning при in-memory compile.
  Evidence: внешний `lua-ecs-moonsharp-probe.ps1`, успешный exit 0; подробности в evidence.
- Observation: generated provider из own source нельзя обнаруживать через output другого generator того же запуска; нужен общий input model в composition emission. Referenced compiled export — отдельный уже поддерживаемый путь сканирования.
  Evidence: `RuntimeCompositionGenerator.cs:116-152,653-731`; проверить собственную интеграцию T1 compilation test.

## Decision Log

- Decision: перенести весь общий binding-слой из ECS.Core в Modding.Core/Bindings, namespace Karpik.Engine.Shared.Modding.Bindings. Lua adapter остаётся в Modding.Lua, scheduling keys — в engine core. Новые project references не нужны; меняются assembly и namespace общего API.
  Date/Author: 2026-10-25 / прямое поручение пользователя; записал Codex. Заменяет первоначальное размещение в ECS.Core ниже.

- Decision: принять только для экспериментального MoonSharp-среза steady-state budget ≤4096+1600*N B/callback после reserve, common C# steady state 0 B. Пользователь явно выбрал «Разрешаю экспериментальный бюджет» после показа 1384+1560*N; рост storage исключён из steady state и должен измеряться отдельно. Это не production real-time endorsement.
  Date/Author: 2026-10-25 / пользователь; записал Codex.

- Decision: Ruling: продолжить на codex/modding-0.7 без worktree и новой ветки — пользователь явно ответил «В текущей ветке делай» на вопрос о зависимой ветке; прежнее ограничение изменено этим указанием. Это смешивает локальную историю с cooked assets; риск попадания лишнего scope в #101 ограничивается запретом push/PR и отдельным учётом diff от 50b1d75.
  Date/Author: 2026-10-25 / пользователь; записал Codex.

- Decision: сохранить контракт передачи и ограничить implementation f32-срезом, без переобсуждения int-ID, pool API и single Lua loop.
  Rationale: дизайн и первый этап уже согласованы пользователем; реализация не поручена.
  Date/Author: 2026-10-25 / пользователь, перенесено Codex.
- Decision: документы сейчас внешние. Будущие назначения: `plans/lua-ecs-bindings-spec.md`, `plans/lua-ecs-bindings-execplan.md`, `docs/superpowers/plans/2026-10-25-lua-ecs-bindings.md`; evidence под `docs/knowledge/investigations/` только после отдельного согласия на knowledge notes. При переносе исправить взаимные relative links.
  Rationale: task-ветка не выбрана; нельзя расширять #101 или автоматически ветвиться от task-ветки.
  Date/Author: 2026-10-25 / Codex, ограничение пользователя.
- Decision: предложить common bindings в существующем ECS.Core; Engine CLR metadata, ModSet canonical schemas, Simulation SoA/query state. Generated provider и typed float operations без object.
  Rationale: существующие project references дают нужное направление зависимостей без новой сборки и MoonSharp в common layer.
  Date/Author: 2026-10-25 / Codex, инженерное предложение.
- Decision: normal last dynamic del снимает anchor; world deletion cleanup чистит только external storage в release hook. Query owns int[] snapshot, не native EcsSpan.
  Rationale: public lifecycle порядок и инвалидируемые native buffers подтверждены source; тесты обязательны.
  Date/Author: 2026-10-25 / Codex, инженерное предложение реализации принятого контракта.
- Decision: не устанавливать численный VM budget без boundary measurements и решения разработчика. Common C# steady state должен дать 0 managed allocations.
  Rationale: свежий raw Lua probe не измеряет bindings; AGENTS запрещает незаявленный hot-path GC.
  Date/Author: 2026-10-25 / Codex.

## Outcomes & Retrospective

Реализован первый экспериментальный f32-срез, общий для CLR и Lua: generated native fields, ModSet schemas, Simulation SoA, mixed snapshots, pool/view ownership, inferred scheduling и две policies. Данные проходят через полноценный Runner в четырёх конфигурациях. Реальная матрица N=0/1/16/256/4096 проверила согласованный потолок; no-GC MoonSharp не заявляется.

Независимый fresh-context review завершён. Авторская регрессия default(EcsComponentAccessDescriptor) подтверждена и исправлена с RED→GREEN; добавлены dynamic-mode и empty-key diagnostics. Замечание о диагностике pool/query контекста исправлено на error path без success allocations. Автоматическое DI-разрешение новых сервисов проверено Static/Dynamic отдельным тестом; 2/2.

[ADR](../docs/02_ADR/lua-ecs-common-bindings.md) фиксирует ownership/data layout/scheduling и принятое экспериментальное исключение по allocations. Ручной запуск реальной игры и NativeAOT publish не выполнялись: сценарий проверен engine fixtures, игровой mod/host отдельно не добавлялся. Source/runtime capabilities более широких типов остаются следующим этапом, не объявлены невозможными. Всё остаётся локально в текущей ветке без публикации.

Итоговые команды из repository root: `dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj --no-restore -m:1 -nr:false` — 72/72; аналогично ECS.Core.Tests — 61/61 и StaticAnalyzer.Tests — 54/54. Runner: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false` — 266/266, после свежей Release сборки/прогона. Source-код между сборкой и финальным no-build прогоном не менялся.

Один предыдущий полный Runner дал 265/266 из-за IOException при cleanup временного bundle в `ProcessManagerLifecycleTests.WorkerRequestedReload_RacingStop_DoesNotStartReplacement`. Без изменений ProcessManager отдельный повтор прошёл 2/2, следующий полный — 266/266. Это наблюдавшийся file-lock сбой проверки, не скрытый pass и не исправление чужого subsystem.

После M1/M2/M3 записывать фактические результаты здесь и в подробном плане. После M4 предложить/оформить ADR для общих lifetime/storage/scheduling решений; measured GC exception фиксировать в ADR только если разработчик её принял. Заметки docs/knowledge автоматически не создавать.

## Context and Orientation

Repository root — `C:\Users\artem\RiderProjects\KarpikEngine`; зафиксирован HEAD 50b1d7586c92c1177a836d7a35e601736e2b4f6b на codex/modding-0.7, ahead 7 по локальному tracking ref. PR #101 remote head 74353a4, base main. Сначала recheck, не reset/checkout поверх чужих изменений. Ожидать merge предпосылок в main либо получить явное согласие на dependent branch с указанным parent; причина и base записываются здесь до внесения файлов в repository. Зависимую ветку сейчас не создавать.

`LuaComponentRegistry.cs` — Engine name→Type, `LuaSystemDeclarations.cs` — parser старых reads/writes, `LuaScriptRuntime.cs` — callbacks и Script.Call, `LuaSimulationPreparation.cs` — load/seal/AddInstance, `Systems/LuaUpdateSystem.cs` — sequential Update adapter. Все эти пути находятся под `Modules/Shared/Modding/Modding.Lua/`.

`Modules/Shared/ECS/ECS.Core/` уже доступен Modding.Core и содержит world integration. Новая папка Bindings здесь будет common, без MoonSharp. `src/Karpik.Engine.Core/Scheduling/` содержит Type-only descriptors и graph compiler. `src/Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/` содержит RuntimeCompositionGenerator и EcsUpdateRegistryGenerator. `src/Karpik.Engine.Core.Runner/` — DynamicCompositionDiscovery, runtime registry и Static composition; использовать их существующие пути регистрации.

SoA означает отдельный массив значений на scalar field; sparse mapping entityId→dense slot и обратный dense entityIds дают O(1) presence/access/removal. Snapshot query — собственные ID на момент entities(), данные остаются live. Anchor — служебный unmanaged CLR component для native presence, не deletion request. ModSet — immutable schemas между Simulation; VM и данные Simulation-owned. Названия C# API в подробном плане являются предлагаемыми внутренними контрактами, не готовым публичным API.

Обязательная семантика: readonly по умолчанию; mutable в exc разрешает add; opt не фильтрует; неизвестный ID всегда startup error. handles/queries/views проверяют активную систему и Simulation на каждом вызове. get/add передвигает persistent view; нет ref через Lua callback. ID — int, без generation protection. NaN и Infinity разрешены; конечное double→f32 overflow отклоняется. Direct writes не откатываются. StopSimulation по умолчанию, LogAndContinue обязателен без disable.

## Real-Time Assessment

Изменения затрагивают Update и scheduler. Pure common C# capture/fields/pools/gates после reserve — 0 B/entity и B/callback, без LINQ/boxing/delegate creation/locks. Dynamic values — плотные float arrays, sparse размер world.Capacity, dense capacity растёт отдельно от PoolComponentsCapacity. Query snapshots — int arrays на named query; резерв при подготовке и resize, не на сущность. Рост может аллоцировать/копировать и измеряется отдельно. Ни ArrayPool rent в каждом callback, ни native unsafe pointers для удержания views не требуются.

MoonSharp неизбежно имеет ненулевую стоимость чисел и интерпретации; измерять базу и добавку отдельно. Никакого обещания zero-GC VM. M0 — gate для измеренного allocation budget; пока число не принято, нет права объявить горячий путь допустимым. Имена превращаются в numeric scheduling keys до pipeline. Существующий sequential режим сохраняет отсутствие параллельных структурных операций во время Lua callback; не пытаться ослабить его в этом срезе.

Все изменения Shared, без Client↔Server утечек. Update phase сохраняется, физику/fixed tick не менять. Изменение schemas/hot reload потребует нового ModSet; migration, save/network и cross-world migration вне этапа. Все новые listeners/scope resources освобождаются идемпотентно.

## Plan of Work

Сначала T0 устанавливает допустимую ветку и поручение, затем измеряет границы MoonSharp в изолированных тестовых probes, не заменяя Lua-логику batch-функциями. Если добавочные allocations требуют компромисса, сохранить измерения и запросить конкретное решение о budget до реализации runtime integration.

T1 расширяет существующий generator binding-моделью закрытых Register<T>, typed accessors и provider emission, связывает own-host static provider с RuntimeCompositionGenerator и проверяет referenced assembly discovery. Common metadata и native adapters находятся в Modding.Core/Bindings; LuaComponentRegistry перенаправляет существующий API в общий registry.

T2 добавляет ModSet schema registry с canonical compare и Simulation storage, anchor и world hooks без правок third-parties. T3 подготавливает mixed queries и snapshot buffers, режимы и callback ownership. T4 расширяет scheduling access identity и оба graph Build пути, сохраняя Type ctor/CLR declarations. T5 связывает parser и Lua adapter с common runtime, вводит две error policies на границе declared callback и полную сквозную проверку. T6 выполняет приёмку и фиксирует фактические результаты/ограничения. Точные файлы, интерфейсы и тесты перечислены в linked tasks; основные acceptance conditions продублированы ниже, чтобы обзор был самостоятельным.

## Milestones

M0 / T0: после разрешения implementation существуют воспроизводимые boundary measurements, записаны B0/b и время, отдельно reserve/growth. Численный VM budget либо принят разработчиком, либо работа остаётся на этом gate. Сейчас выполнен только raw-number probe, не M0.

M1 / T1–T2: generator test действительно компилирует и исполняет Position accessor из referenced assembly, common tests доказывают shared schemas и isolated data, anchor iff dynamic count>0, release cleanup до ID reuse, resize и default. Проверить generator и relevant Runner tests после каждого T, записать команды и результаты.

M2 / T3–T4: common mixed query проходит membership/snapshot/ownership tests; scheduler synthetic descriptors доказывают native/dynamic Read/Write независимо от sequential, оба graph entry points проходят regression. Запустить targeted Runner и ECS.Core suites; без успеха не начинать M3.

M3 / T5: реальный Lua callback даёт ожидаемые Temperature во всех Static/Dynamic × Deterministic/Parallel случаях, legacy поведение сохранено, обе policies и partial writes доказаны, fresh full-slice allocation results укладываются в принятое M0 ограничение. Если измерение бюджета изменилось — снова согласовать, не расширять молча.

M4 / T6: relevant full suites проходят без пустых фильтров; выполнен или явно отмечен как непроверенный ручной сценарий; документы и ADR актуальны. Отдельного production-ready заявления при непроверенном важном условии нет.

## Concrete Steps

Все repository команды выполнять из `C:\Users\artem\RiderProjects\KarpikEngine`. При отдельном разрешённом worktree вместо этого явно использовать его root. Ниже future acceptance commands; в текущем чате они НЕ выполнялись, кроме git checks и external probe.

```powershell
git status --short --branch --ignore-submodules
git rev-parse HEAD
dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -m:1 -nr:false --filter FullyQualifiedName~ClrComponentBinding
dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --filter 'FullyQualifiedName~DynamicComponent|FullyQualifiedName~MixedComponentQuery'
dotnet test tests/ECS.Core.Tests/ECS.Core.Tests.csproj -m:1 -nr:false --filter FullyQualifiedName~EcsUpdateGraphBuilder
dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --filter 'FullyQualifiedName~LuaEcsBindings|FullyQualifiedName~LuaSystemRegistration|FullyQualifiedName~LuaPreparation'
dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false --filter FullyQualifiedName~LuaEcsAllocation
```

Ожидать ненулевое число matching tests и 0 failed; allocation suite должна печатать измеренную таблицу и проверять budget, не только Assert(bytes>0). До появления соответствующих tests команда с пустым фильтром не считается проверкой. Новые классы названий фиксируются в tasks. Каждая test-команда включает сборку минимального test-проекта с single-node/no-reuse; полный solution build не нужен. После T6 дополнительно выполнить без filter те же Generator, Runner, ECS.Core и `tests/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj`, затем `git diff --check`. Это ожидаемые результаты, не текущие pass counts.

## Validation and Acceptance

Сквозной fixture содержит e1 Position.Y=0, e2 Position.Y=100, e3 Position.Y=200 + Temperature.Value=99, e4 без Position с другим native компонентом. Первый callback даёт e1=20, e2=19, e3 остаётся 99, e4 без Temperature; pending snapshot contains exactly e1/e2. Следующий entities() пуст. Исходные Position не меняются.

Отдельные tests: неизвестный component ID в inc/exc/opt; readonly add/del/set; mutable exc; opt без entities; native/dynamic smallest inc и пустой inc; snapshot при добавлении/удалении и world resize; вложенные разные queries; повторный entities() одного имени начинает заново; break/error не отравляют следующий callback; другой owner/Simulation/disposed access; NaN/Infinity/конечный overflow/нечисловой setter; Release ID validation.

Lifecycle: swap-remove не меняет чужое значение; повторный add default; 2 схемы разных модов держат один anchor; последний dynamic del убирает anchor; если иных native нет, применяется штатная auto deletion; native DelEntity + partial/full ReleaseDelEntityBuffer очищает данные до reuse; destroy/dispose не вызывает recursive delete; две Simulation разделяют только metadata, failed first preparation не коммитит partial schema catalog; changed schema on later Simulation fails startup.

Scheduling: union всех queries, Write dominates Read, distinct dynamic IDs не конфликтуют случайно; CLR alias имеет тот же key; presence filters учтены; anchor Write выведен; read/read может идти независимо при synthetic non-sequential, read/write и write/write имеют edge. Lua/native реальный порядок и обе policies проверяются в 4 комбинациях composition/scheduler, но не используются вместо synthetic key tests.

При LogAndContinue ошибка после temperature:add оставляет default/предыдущие записи, следующая система запускается, эта система повторяется в следующем кадре; StopSimulation прерывает последующие системы текущего update и доводит ошибку до stop path host. Startup errors блокируют обе policies. Ручной сценарий после реализации: загрузить Lua-мод с указанным fixture, проверить значения CLR-инспекцией, disable/re-enable, пересоздать Simulation, уничтожить её; это не заменяется historical tests.

## Idempotence and Recovery

Source audit и git checks безопасно повторять. Snapshot capture каждый раз строит новый набор в собственном reserve buffer. Freeze registry атомарен; повторная Simulation только сравнивает schema descriptions. Отмена/ошибка подготовки освобождает pending runtimes/storage без публикации partial catalog; VM не запускается повторно для сбора схем.

При runtime error direct writes остаются по контракту; rollback gameplay state не выполнять. Gate снимается finally. Storage.Dispose и world.OnWorldDestroy не double-free и не вызывают друг друга рекурсивно. При ошибке ветки/чужом dirty tree остановить только repository mutation, сохранить внешний пакет; ничего не reset/stash и не удалять чужое.

Для отката будущей реализации возвращать связанную интеграцию parser/descriptor/codegen/storage вместе, не оставлять старый parser с новым scheduling или наоборот; конкретный commit revert выполнять только по отдельному поручению. Новый ModSet нужен при изменении schemas. Нельзя переносить данные между schema versions автоматически.

## Artifacts and Notes

Все текущие документы находятся вне git рядом с этим файлом. Spec задаёт контракт, tasks — чек-лист реализации, evidence отделяет source facts от предложений. Probe воспроизводим отдельным ps1. При переносе в допустимую task-ветку изменить относительные ссылки по назначению из Decision Log, сохранить self-contained текст и проверить links.

Никаких commits/push/PR автоматически. Следующее действие сейчас — ревью этого пакета и решение о базе ветки; исполнение T0–T6 требует отдельного явного поручения.
