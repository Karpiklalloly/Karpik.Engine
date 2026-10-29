# Расширить ECS bindings типами bool, i32, u32 и f64

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

Дата: 2026-10-29, Europe/Samara. [Спецификация и каталог](lua-ecs-type-catalog-spec.md). [Подробный план](../docs/superpowers/plans/2026-10-29-lua-ecs-type-catalog.md).

## Текущее состояние

- Статус: выполняется M1/T1; контракт и performance policy согласованы.
- Сейчас: полный финальный Runner 390/390 и Generator 76/76 прошли на восстановленном fixture и с legacy F32 init/with; готовится итоговое review.
- Далее: независимое review и его необходимые исправления; изменения остаются локальными.
- Блокеры: нет. Пользователь явно разрешил текущую ветку сообщением «в этой ветке делай»; отдельная ветка не создаётся.

## Purpose / Big Picture

Позволить Lua читать и изменять native и dynamic bool/int/uint/double поля в тех же mixed queries, которые уже поддерживают float. Данные остаются типизированными: integer не усекается из дроби, double не теряет точность через float, boolean не использует Lua truthiness. Каталог явно отделяет реализованное f32, следующий срез и более поздние возможности.

Исходное поручение создавало только документы. Последующее «Делай» разрешило реализацию и приняло контракт/бюджет. Ни одна implementation checkbox не отмечается на основании подготовки плана или preflight checks.

## Progress

- [x] (2026-10-29) Подтверждён scope: «Каталог и план; код после согласования».
- [x] (2026-10-29) Исследованы типизированные accessors, схема, SoA pool, Lua adapter, actual codegen и все найденные F32 callers/tests. Baseline `8fae435f561b022d5b74aaa74cafc910b1f0f37a`; локальные main/origin/main `bed23cc`.
- [x] (2026-10-29) Созданы каталог/спецификация и связанная пара планов; production и tests не менялись.
- [x] (2026-10-29) Независимый builder-review: один P2 о pass/fail mixed-five allocations; устранён явным предлагаемым критерием `mixed-five <= five-f32 baseline` при каждом N. Иных важных вопросов reviewer не сообщил. Это не runtime verification.
- [x] (2026-10-29) Итоговая автоматическая проверка: PASS — 3 документа, 11 относительных ссылок, 13 обязательных секций ExecPlan, задачи T0–T4, 18 путей owning sources/tests. HEAD остался `8fae435`; `git status --short --branch --ignore-submodules` не показал изменений файлов верхнего репозитория. Состояние submodules этой командой не проверяется.
- [x] (2026-10-29) Последующее «Делай» принято как согласование контракта, performance policy и поручение реализации. Локальные HEAD/main повторно проверены: 8fae435/bed23cc; common bindings descriptor отсутствует в main. Явное разрешение dependent branch ещё не получено; runtime edits не начинались.
- [x] (2026-10-29) Remote preflight через GitHub: чтение AGENTS.md из main успешно, чтение prerequisite ComponentFieldDescriptor.cs из main — 404. Отсутствие path не объясняется общей недоступностью repository.
- [x] (2026-10-29) Baseline common storage/query Release: 14/14, failed 0, exit 0. Команда: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter "FullyQualifiedName~DynamicComponentStorageTests|FullyQualifiedName~MixedComponentQueryTests" --blame-hang --blame-hang-timeout 60s --blame-hang-dump-type none`, cwd исходный root; VSTEST_CONNECTION_TIMEOUT=180. Сборка свежая из предыдущего preflight. Первый, более широкий запуск с LuaEcsBindings был прерван собственным лимитом subprocess 50s после сборки/start тестов; PASS для него не заявляется.
- [x] (2026-10-29) M0 / T0: spec/policy/implementation утверждены; «в этой ветке делай» разрешает текущую codex/modding-0.7. Документы перенесены в repository, relative links исправлены. Baseline 8fae435, storage/query 14/14.
- [x] (2026-10-29) M1 / T1: typed descriptor/view/schema и mixed-kind SoA. RED: missing-API compiler diagnostics; record ToString regression отдельно 1/1 failed (InvalidOperationException) → исправлена. Итоговый targeted Release 20/20, exit 0, включая warmed common 0 B. Команда Runner из T1 с добавлением `FullyQualifiedName~TypedComponentBindingsTests` в filter.
- [x] (2026-10-29) M2 / T2: typed CLR generator и readonly/unsupported matrix. Runtime regression RED KEB001 bool → full Generator Release 76/76 exit 0, команда T2. Existing referenced/own Static/Dynamic tests сохранены. Common M1 commit 4e8fb5d; codegen фиксируется отдельным commit.
- [x] (2026-10-29) M3 / T3: strict Lua tokens и typed getters/setters. Runtime RED: only f32 на первом bool field → GREEN: semantic matrix 87 cases + real Runner mixed lifecycle Static/Dynamic × Deterministic/Parallel 4 cases = 91/91 Release, exit 0. Проверены invalid native/dynamic writes, integer ranges/fractions, double precision/nonfinite/negative zero, schema conflicts, readonly native field и partial writes/policies. Codegen M2 commit 5699dc9.
- [x] (2026-10-29) Первый полный Runner Release 389/389, exit 0, включая все allocation gates. Copy single-field = 1984+1368*N B/callback; mixed-five и matched five-f32 = 1984+2136*N при каждом N. p50/p95/p99/Gen0 и cold growth — [evidence](lua-ecs-type-catalog-evidence.md). Старый f32 ceiling не повышался. Наблюдались warnings cleanup worker shadow directories, failed tests не было.
- [x] (2026-10-29) Legacy F32 init/with восстановлен с kind/null guards: compile RED CS0200 → targeted common Release 21/21, exit 0. Предыдущая идея удалить init отвергнута как ненужная несовместимость; constructor/getter/Deconstruct и record cloning/formatting tests сохранены.
- [x] (2026-10-29) Typed lifecycle negative control: временно убран query preparer Dispose только из test fixture; 4/4 cases failed «No exception was thrown». Fixture восстановлен в finally, git diff по нему пуст. Финальный test build должен пересобрать восстановленный fixture.
- [x] (2026-10-29) Финальный rebuild/test на восстановленном fixture и совместимом F32 init: полный Runner Release 390/390 exit 0; полный Generator Release 76/76 exit 0. Команды `dotnet test <project> -c Release --no-restore -m:1 -nr:false --blame-hang --blame-hang-timeout 180s --blame-hang-dump-type none`; cwd repository root, VSTEST_CONNECTION_TIMEOUT=180. Logger console normal для Runner/minimal для Generator. Новые dependencies не добавлялись.
- [ ] M4 / T4: allocation evidence, lifecycle regression, полные relevant suites, итоговое review и документация.

## Surprises & Discoveries

- Observation: фактический generator находится в `src/Karpik.Engine.Core.Codegen/ClrComponentBindingGenerator.cs`, хотя test project называется `Karpik.Engine.Core.Generator.Tests`.
  Evidence: source inventory и Execute/Fields emission в baseline.
- Observation: `ComponentFieldDescriptor` — positional record только с F32; простой enum switch в Lua недостаточен. Registry тоже явно отвергает не-F32.
  Evidence: ComponentFieldDescriptor.cs:3, ComponentSchemaRegistry.cs:28, LuaComponentDeclarations.cs:33.
- Observation: новые `int` поля изменят прежнее ожидание KEB001 в generator regression.
  Evidence: `ClrComponentBindingGeneratorTests.ClrComponentBinding_RejectsUnsupportedPublicField`.
- Observation: текущий architecture graph устарел, QMD не дал релевантных совпадений. Использованы реальные owning sources и существующие спецификация/ADR.

## Decision Log

- Decision: сейчас только каталог и план; хранить review-пакет вне repository до выбора законной branch base.
  Rationale: прямое уточнение пользователя; текущая modding-ветка объединяет prerequisites, которых ещё нет в main. Новая зависимая ветка без разрешения нарушила бы AGENTS.md.
  Date/Author: 2026-10-29 / пользователь (scope), Codex (размещение пакета).
- Decision: предложить typed managed columns и существующий `IComponentField<T>`, без object/double общего хранилища и Unsafe arena.
  Rationale: сохраняет точность, data layout и текущую модель владения при небольшом изменении.
  Date/Author: 2026-10-29 / Codex; ожидает утверждения.
- Decision: не переносить существующий f32 GC waiver автоматически на произвольные workloads.
  Rationale: число операций и поведение VM определяют B/entity; нужны сопоставимые probes и явное принятие policy.
  Date/Author: 2026-10-29 / Codex; ожидает утверждения.

## Outcomes & Retrospective

Документационный этап завершён отдельно, затем пользователь поручил реализацию в текущей ветке. Добавлены typed common storage/view, CLR codegen и строгий Lua adapter, regression/probe tests. Внешние данные остаются Simulation-owned SoA; no new dependencies, side boundaries или tick changes. Manual game, NativeAOT publish и editor reload/state restore не выполнялись.

Builder-review завершён, его единственный существенный вопрос устранён в spec и T4. Итоговая автоматическая проверка всех 11 относительных ссылок, секций ExecPlan, task IDs T0–T4, Markdown fences и 18 owning source paths прошла; исходный HEAD и status верхнего repository сохранены. Это проверка документов, не tests продукта.

## Context and Orientation

Рабочая база: `C:/Users/artem/RiderProjects/KarpikEngine`, `codex/modding-0.7` at `8fae435`. Codegen компилируется из `src/Karpik.Engine.Core.Codegen`; тесты — `tests/Karpik.Engine.Core.Generator.Tests`. Common bindings находятся в `Modules/Shared/Modding/Modding.Core/Bindings`; Lua adapter — соседний `Modding.Lua`. Sparse mapping связывает entity ID с плотным индексом, SoA хранит поля в отдельных typed массивах. Prepared view — переиспользуемый доступ к текущему entity, а не snapshot значения.

Путь source inspection и полная таблица change points находятся в подробном плане. Prerequisites: работающий f32 common layer, numeric scheduling keys, generated providers и Lua preparation/lifecycle. При переносе на новый main их наличие проверяется заново.

## Real-Time Assessment

Read/write выполняются в callback, то есть hot path. Common C# после подготовки/capacity — 0 B; новые descriptors и columns создаются при подготовке, рост измеряется отдельно. T[] сохраняет плотные данные и точные типы. Tagged metadata добавляет startup/per-field память, не per-entity объект. Ни reflection, ни boxing скаляра, ни lock не добавляются в success path. Пять nullable typed slots дескриптора — допустимый простой вариант, его overhead фиксируется как metadata.

Границы Shared/Client/Server и Engine/ModSet/Simulation неизменны. Fixed dt, scheduler identity и sequential VM execution не меняются. Readonly и mutable виды не сливаются; snapshot membership, native deletion, anchor и int-ID reuse сохраняются. Ошибки/рост не включать в steady-state budget. Lua VM по-прежнему аллоцирует; первый f32 waiver не означает production suitability.

## Plan of Work

Сначала утвердить [контракт](lua-ecs-type-catalog-spec.md), затем выполнить T1–T4 из [подробного плана](../docs/superpowers/plans/2026-10-29-lua-ecs-type-catalog.md). T1 расширяет ComponentFieldDescriptor, SchemaScalarKind, PreparedComponentView и один dynamic pool. T2 расширяет existing CLR generator. T3 добавляет Lua token/Kind dispatch и строгие setters. T4 проверяет lifecycle, allocation costs и общую совместимость, затем обновляет существующие docs/ADR.

## Milestones

M0 завершается явным утверждением и проверкой branch prerequisites. M1 завершается targeted storage/query tests и C# 0 B. M2 завершается полным Generator suite и проверкой реального typed read/write в emitted assembly. M3 завершается native/dynamic сценариями и Static/Dynamic × Deterministic/Parallel. M4 завершается relevant полными suites, записанными измерениями и закрытыми review findings. Фактические команды и exit codes заносить сюда и в подробный план после каждого milestone.

## Concrete Steps

Все команды реализации выполняются из repository/task-worktree root; baseline этого исследования — `C:/Users/artem/RiderProjects/KarpikEngine`. В новой worktree использовать её root, а не старый checkout.

1. `git status --short --branch --ignore-submodules` и `git rev-parse HEAD` — проверить исходное состояние и сохранить чужие изменения.
2. Проверить наличие описанных prerequisites на выбранной базе и выполнить M0. Не переносить файлы на integration branch напрямую.
3. T1: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-restore -m:1 -nr:false --filter "FullyQualifiedName~DynamicComponentStorageTests|FullyQualifiedName~MixedComponentQueryTests"`.
4. T2: `dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -c Release --no-restore -m:1 -nr:false`.
5. T3/T4: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-restore -m:1 -nr:false`.

Ожидается exit 0, отсутствие failed tests и записанное количество фактически выполненных тестов. Если assets отсутствуют, сначала targeted `dotnet restore` соответствующего project, затем повтор; не подменять результат старой сборкой через `--no-build`.

## Validation and Acceptance

Приёмка документации: три файла существуют, относительные ссылки разрешаются, ExecPlan содержит все обязательные секции, все change-point paths существуют в исследованной базе. Приёмка реализации: строгие boundaries из спецификации, полный mixed result в native и dynamic, owner/readonly/disposal guards, точность double, scalar kinds в schema equality, no regression f32, reserved common path 0 B, отдельные измерения Lua workload. Все пять Review Focus случаев распределены по T1–T4. Не считать generation test доказательством настоящего NativeAOT publish.

## Idempotence and Recovery

Чтение/проверка пакета повторяемы. После согласования скопировать документы только в законную task branch с проверкой diff. Не cherry-pick всю modding-ветку ради документации. Если prerequisites не merged, запросить явное dependent-branch исключение с parent/target; после merge parent retarget PR на main и проверить inherited diff. Если проверки выявили semantic/performance проблему, сохранить evidence и скорректировать оба плана; не поднимать бюджет и не возвращаться к boxed/double storage автоматически.

## Artifacts and Notes

Артефакты: [каталог](lua-ecs-type-catalog-spec.md), [подробные задачи](../docs/superpowers/plans/2026-10-29-lua-ecs-type-catalog.md), [измерения](lua-ecs-type-catalog-evidence.md), этот обзор; ledger в игнорируемом `.superpowers/sdd/2026-10-29-lua-ecs-type-catalog/`. Все пути уже repository-local, relative links обновлены. QMD CLI доступен, но `qmd collection list` не содержит коллекций; индекс не создавался автоматически. Сохраняется запрет публикации базового Lua/ECS плана: без push/PR.
