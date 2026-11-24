---
status: completed
complexity: complex
complexity_source: auto
development_mode: tdd
source_design: modding-types-design.md
design_revision: 15
plan_revision: 1
approved_plan_revision: 1
approved_design_revision: 15
---

# Завершить каталог типов CLR/Lua bindings по проверяемым этапам

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Разработчик сможет зарегистрировать unmanaged CLR components с обычными скалярами, enum, точными числами, nested structs, native fixed buffers и opt-in pointers; моды смогут объявлять эквивалентные dynamic schemas и работать с ними через nested Lua views. Явные copy/new_snapshot/copy_to дают отделённые данные без клонирования ECS entity. Точный поведенческий контракт: source design revision 15, решения D1–D16 и «Итоговый контракт». Этот документ содержит исполнимые work packages и проверки, а не разрешение реализации.

## Progress

- [x] (2026-11-11) Исследованы актуальные common bindings, schema parser, Lua descriptor и CLR generator; восстановлен предыдущий typed план.
- [x] (2026-11-11) С пользователем согласованы scope и Lua contracts; design revision 15 сохранён отдельно.
- [x] (2026-11-11) Standalone MoonSharp custom descriptor probe: 10/10 результатов, 816+528*N B/callback для exact-add loop. Это не engine acceptance.
- [x] (2026-11-11) Проверено отсутствие common bindings prerequisite в локальном main d8ee920; current parent codex/modding-0.7 at 4767dca.
- [x] (2026-11-11) Пользователь: «Делай в этой ветке» — единое утверждение реализации/budget с заменой dependent branch на текущую codex/modding-0.7.
- [x] (2026-11-11) WP-00: текущая ветка явно разрешена, документы перенесены; fresh Release baseline 134/134, exit0. Restore offline с NUGET_PACKAGES и RestorePackagesPath=/home/artem/.nuget/packages; VSTest outside sandbox из-за loopback SocketException(13).
- [x] (2026-11-11) WP-01: scalar common/generated/Lua tests passed в fresh focused run; old five kinds/record compatibility сохранены.
- [x] (2026-11-11) WP-02: exact constructors/operators tests50/50 и ScalarCatalog24/24 прошли в repository focused run; common0B/exact Lua gates пройдены.
- [x] (2026-11-11) WP-03: recursive canonical schemas, nested/fixed native+dynamic и parser6/6/Composite3/3/Native2/2/LuaComposite25/25 пройдены.
- [x] (2026-11-11) WP-04: ComponentSnapshot8/8 и Lua snapshot identity/lifetime/0B-copyTo пройдены.
- [x] (2026-11-11) WP-05: PointerPolicy8/8, LuaPointer7/7, native/dynamic roundtrip/revocation и0B policy checks пройдены.
- [x] (2026-11-12) WP-06: fresh Runner583/583, Generator78/78, independent review/R1/R2 closed, evidence/API/ADR updated.

## Surprises & Discoveries

- Main и origin/main локально указывают d8ee920; git ls-tree не находит ComponentFieldDescriptor.cs. Ветка от main не содержит необходимую базу, а parent содержит исторические изменения помимо моддинга. Первоначально требовалась dependent branch; пользователь явно разрешил текущую ветку. Для локальной приёмки учитывается только task diff; публикация не поручена.
- MoonSharp custom descriptor поддерживает точную арифметику без double fallback, но immutable Lua result в простом probe стоит 528 B/сложение. Нельзя объявлять этот путь production no-GC.
- PreparedComponentView находится в PreparedComponentHandle.cs и переиспользуется на pool handle. Snapshot нельзя реализовать как ссылку на этот view.
- LuaComponentDeclarations и ComponentSchemaRegistry пока flat scalar-only; recursive schema validation должна появиться до runtime views.
- Architecture graph at 7224b183 не свеж для current HEAD; текущие facts установлены targeted source inspection. QMD tool в текущем наборе не доступен; использованы первичные локальные specs/ADR.

## Decision Log

- 2026-11-11 / пользователь: весь каталог, по этапам; nested views; explicit detached copy/copy_to; индексы 1..N; host-only opaque pointers; Simulation lifetime snapshots; exact constructors и checked same-kind arithmetic; ^/bitwise вне текущего operator set.
- 2026-11-11 / автор плана: auto complexity complex из-за public common API/codegen/VM/lifetime integration; auto development mode tdd для observable behavior. Документы/branch setup и исследовательские probes — normal с прямой проверкой.
- 2026-11-11 / пользователь: «Делай в этой ветке» заменяет первоначальное предложение dependent branch. Используется текущая codex/modding-0.7; локальный task diff отсчитывается от4767dca. Публикация не поручена, существующий PR не расширять автоматически.
- Предложение для единого утверждения: experimental Lua canonical ceiling 4096+1600*N*L+544*N*O B/callback. L — leaves, прочитанные и записанные по одному разу на entity, O — exact arithmetic operations/entity. N=0/1/16/256/4096. Existing f32/typed gates не повышать. Это ограничение canonical workloads, не любых пользовательских скриптов. Common C# и warmed copy_to остаются 0 B.
- Публикация не входит в это утверждение: текущие modding handoff сохраняют no-push/no-PR. После реализации подготовить title/body/verification и показать непубликованный результат; новый push/PR возможен при отдельном явном поручении пользователя. Требование AGENTS.md про отдельный feature diff сохраняется.

## Outcomes & Retrospective

2026-11-12: весь согласованный type catalog реализован в явно разрешённой текущей codex/modding-0.7. Добавлены small/char/enum/exact/native-width kinds, recursive native/dynamic nested/fixed access, independent snapshots и reusable copy_to, host-only opaque pointer type/domain policy. Старые scalar APIs/record compatibility и allocation gates сохранены. Runtime common read/write/copy_to после подготовки0B; Lua experimental, factory/parsing/formatting/cold costs измерены отдельно.

Окончательная приёмка: fresh Release Runner583/583, Generator78/78, exit0; полный source соответствует production/test fingerprint в [evidence](modding-types-evidence.md). R1 InlineArray и R2 manual registered-service construction исправлены; independent reviewer закрыл оба, open findings0. [API справочник](../docs/modules/modding/typed-bindings.md) и [ADR](../docs/02_ADR/lua-ecs-common-bindings.md) обновлены. Есть existing build warnings, новых зависимостей/side imports не добавлено. No push/PR, чужой IDE файл сохранён.

NativeAOT,32-bit runtime/manual game/editor reload/save/network не проверялись этим срезом. Native-width bounds подтверждены на текущем64-bit host и checked native casts; отдельный helper, зеркалящий CLR cast ради искусственной32-bit проверки, не добавлялся. Нет утверждения32-bit runtime acceptance.

Предлагаемые learnings для docs/knowledge (не записаны автоматически):1) reusable live view и detached snapshot — разные lifetime contracts;2) exact double→decimal conversion проверяется рациональной представимостью, не rounded round-trip;3) opaque pointer token не владеет памятью и требует host revoke до free;4) scalar metadata accessor обязан проверять kind даже при нескольких typed interfaces;5) unsupported native layouts нужно диагностировать целиком, а не молча открывать backing field.

## Context and Orientation

Cwd: /home/artem/git/Karpik.Engine. Parent HEAD на момент исследования 4767dca; единственный замеченный посторонний untracked файл .idea/.idea.KarpikEngine/.idea/.name. Не удалять/не коммитить его.

Common runtime: Modules/Shared/Modding/Modding.Core/Bindings/{ComponentFieldDescriptor.cs,IComponentField.cs,ComponentSchemaRegistry.cs,DynamicComponentPool.cs,DynamicComponentStorage.cs,PreparedComponentHandle.cs,ComponentQueryPreparer.cs,ClrComponentPool.cs,ClrComponentBindings.cs,IComponentPoolAccess.cs}. Schema каталог ModSet-owned, mutable data Simulation-owned. Типизированные T[] columns разделяют scalar leaves; sparse mapping связывает entity ID с dense row. Live view — переиспользуемый доступ, не снимок.

Lua: Modules/Shared/Modding/Modding.Lua/{LuaEcsContext.cs,LuaComponentDeclarations.cs,LuaSimulationPreparation.cs,LuaScriptRuntime.cs}. Пользовательский types module должен быть зарегистрирован до исполнения entry scripts с сохранением sandbox. Использовать explicit custom descriptors; не reflection automatic interop.

Generator: src/Karpik.Engine.Core.Codegen/ClrComponentBindingGenerator.cs и integration RuntimeCompositionGenerator.cs; tests/Karpik.Engine.Core.Generator.Tests/ClrComponentBindingGeneratorTests.cs. Существующие public native fields обнаруживаются через Register<T>, providers существуют own/referenced и Static/Dynamic paths.

Runner tests: tests/Karpik.Engine.Core.Runner.Tests/{TypedComponentBindingsTests.cs,LuaTypedBindingsTests.cs,LuaTypedAllocationTests.cs,LuaEcsAllocationTests.cs,DynamicComponentStorageTests.cs,MixedComponentQueryTests.cs,LuaSimulationLifecycleTests.cs,LuaPreparationTests.cs}. Добавлять отдельные тестовые классы для новых meaningful contracts, не дублировать implementation.

Термин token означает opaque pointer value с type/domain/generation; не CLR object внутри ECS-компонента. Snapshot означает независимые данные одного компонента, не новую entity и не query membership snapshot.

## Real-Time Assessment

Все getters/setters/copy_to вызываются в hot paths. Общий слой: только типизированные значения, dense arrays и ref/native accessors, без per-call boxing/reflection/LINQ/locks; 0 B после reserve. Nested views, field lookup metadata и fixed element wrappers готовятся до measured access, а snapshots заранее перед reuse. Для fixed leaf columns использовать dense layout с известным stride; не per-entity object graphs. Не сохранять pointers/refs на native movable elements между вызовами. Измерять metadata и подготовку отдельно; не расширять descriptor большим числом unused reference slots без оценки cache/памяти.

Lua аллоцирует даже в обычном VM исполнении. Новый предлагаемый experimental потолок применяется к canonical workloads и проверяется отдельно от common 0 B. copy/new_snapshot, parsing/string formatting, growth и startup allocation — отдельные измерения, не скрытая часть hot-path обещания. Immutable exact Lua values не заменять mutable переиспользуемыми объектами ради прохождения gates.

Shared не зависит от Lua types и Client/Server. DI constructor-only, Engine/ModSet/Simulation ownership сохраняется. Новых tick/dt правил, scheduler или parallel execution Lua VM не добавлять. Host pointer domain checks не удерживают native memory и не доказывают её живость; ответственность host должна быть явной в API. No filesystem/network/process API для модов не расширяется.

## Plan of Work

Каждый WP завершается targeted validation до следующего зависящего milestone. Сначала добавить failing behavioral tests и установить правильную причину Red, затем минимальный runtime/generator код и Green. Не менять actual third-party DragonECS/MoonSharp sources. Основные точки правок и acceptance ниже; новые helpers размещать в соответствующих существующих модулях.

### WP-00 — Task isolation и baseline

- Status: completed
- Goal: Подготовить feature diff без inherited изменений и исполнимую baseline проверку.
- Scope: После явного утверждения перепроверить git state, сохранить текущую явно разрешённую codex/modding-0.7, записать task base SHA4767dca; перенести два подготовленных документа в plans/modding-types-design.md и plans/modding-types-execplan.md и исправить source_design link. Сохранить probe source/evidence как task artifacts. DOTNET_CLI_HOME направить в writable /tmp.
- Boundaries: Нет runtime behavior, parent edits, reset/clean, push/PR. Не выполнять fetch/merge автоматически, не переписывать чужие commits.
- Depends on: none
- Acceptance: AC1, AC5 (baseline/isolation).
- Verification: git status/diff relative to base; targeted current TypedComponentBindings/LuaTypedBindings/ClrComponentBindingGenerator suite. Сохранить фактические build/test results; historical 390/390 и 76/76 не считать текущими.
- Development mode: normal для branch/docs, baseline tests без выдуманного Red.

### WP-01 — Small scalars, char и enum

- Status: completed
- Goal: i8/u8/i16/u16/char и CLR enums с <=32-bit underlying проходят common/native/dynamic/Lua boundaries.
- Scope: Append kinds без перенумерации existing SchemaScalarKind; additive typed accessors/descriptors, common columns и schema tokens; generator underlying casts без boxing; strict Lua range/type validation. Char code unit 0..65535 включая surrogate. Dynamic enum node хранит underlying kind без named-members registry.
- Boundaries: No exact 64-bit fallback via double, nested structs, pointers. Unsupported kinds диагностируются до запуска, поля не скрываются.
- Depends on: WP-00
- Acceptance: AC1, AC2, AC3, AC10.
- Verification: signed/unsigned endpoints, ±1 вне диапазона, fractions/nonfinite/strings/bools, unknown enum values/flags, readonly и invalid writes unchanged; generated own/referenced providers и output compilation. Release common reserved allocation tests 0 B.

### WP-02 — Exact values и process width

- Status: completed
- Goal: i64/u64/decimal/nint/nuint и wide enums сохраняют точность в storage, Lua и generated access.
- Scope: Extend common typed columns/accessors; register explicit immutable Lua exact descriptors и types.i64/u64/decimal constructors до entry script execution. Arithmetic/comparisons/tostring согласно design, checked overflow, CLR integer division/remainder, CLR decimal arithmetic; no implicit mixed-kind conversions. Process-width checks для nint/nuint; not pointer tokens.
- Boundaries: ^/bitwise отсутствуют; нет reflection binder/boxing scalar в common. Не переиспользовать mutable number wrapper. Для decimal number constructor доказать точность преобразования уже представленного binary rational, не сравнивать только rounded double round-trip. Строковый parsing invariant, finite/range checked; policy для незаявленных форм строк фиксируется tests как строгая ошибка.
- Depends on: WP-01
- Acceptance: AC1, AC2, AC3, AC4, AC6.
- Verification: long/ulong extrema и значения вокруг 2^53; decimal scale/bounds, exact string round-trip, fractional integer cast errors, decimal number 0.1 double не проходит как математически точный decimal 0.1; checked min/-1 division, zero division, unsigned unary minus, mixed-kind ordering/equality; 100–1000 deterministic generated CLR oracle cases без новой зависимости. Native process-width path проверить на текущем host, альтернативную ширину — чистыми validation helpers без ложного объявления 32-bit runtime run. Standalone versus engine canonical exact-add/copy allocation probes.

### WP-03 — Recursive schemas и nested/fixed access

- Status: completed
- Goal: view.Position.X и view.Points[1].X работают как typed reusable access с canonical schemas.
- Scope: Evolve schema model, preserving existing scalar constructors and generated provider consumers. Validate/canonicalize inline struct/fixed/enum nodes; flatten scalar leaves для common columns, fixed strides с checked sizes. Extend prepared metadata и generated direct nested leaf/native fixed access. Lua nested/indexed descriptors создаются заранее, #buffer и 1..N bounds. Обновить parser diagnostics, error context full field/index path.
- Boundaries: Native InlineArray/private fields/properties и dynamic named-type registry не входят. Не хранить boxed nested struct copies, per-entity nested objects, escaping fixed pointers. Public existing f32 constructor/getter/init/Deconstruct/with behavior не ломать молча; добавить composite APIs отдельно либо совместимо.
- Depends on: WP-02
- Acceptance: AC1, AC2, AC3, AC4, AC7, AC10.
- Verification: nested readonly parents/leaves, native fixed first/last/invalid index, arrays of dynamic structs, scalar/enum/exact leaf matrix, scalar→struct/layout conflicts, canonical field order, duplicate/unknown/cyclic schema data, checked length overflow. Live nested alias follows second get; callback/disposal guards survive. Dense swap/world resize и row moves сохраняют весь nested/fixed data. Common warmed traversal 0 B.

### WP-04 — Detached snapshots

- Status: completed
- Goal: copy/new_snapshot/copy_to поддерживают независимые nested/fixed данные до disposal Simulation.
- Scope: Add snapshot data/access contract в common layer и Lua pool operations. new_snapshot factory создаёт default buffer в active callback; copy создаёт и заполняет; copy_to проверяет identity до изменения и переиспользует данные/views. Собственное typed snapshot storage, CLR readonly propagation, Simulation lifetime guard без списка сильных references, удерживающего все когда-либо созданные snapshots до shutdown.
- Boundaries: No entity cloning, implicit ECS writeback, migration/save serialization или между-Simulation transfer. Query readonly gate не обходится через snapshot. Snapshot не привязывать к reusable source view.
- Depends on: WP-03
- Acceptance: AC1, AC2, AC3, AC4, AC8.
- Verification: source get(B)/write/delete/resize не меняют snapshot A; nested mutation snapshot не меняет source; copy_to повторно сохраняет identities root/nested/fixed views; mismatch component/schema/Simulation rejected before overwrite; unused new_snapshot default values; captured snapshot data valid across callbacks и invalid после Simulation disposal. Common warmed copy_to 0 B. Creation memory и Lua wrapper costs отдельно, не подменять measured allocation native-only данными.

### WP-05 — Host-only pointers

- Status: completed
- Goal: Native и dynamic pointer fields разрешены только при explicit host policy и работают как opaque tokens.
- Scope: Constructor-injected type/domain registry на нужных existing scopes, не static state/service locator. CLR generator выдаёт opt-in pointer metadata/accessors; dynamic schema pointer aliases разрешаются до callback. Store unmanaged pointer/type/domain-generation values в common columns/snapshots. Lua nil/null и immutable opaque value, compatible same-domain assignment, revocation и disposal checks до записи.
- Boundaries: No dereference/address arithmetic/raw-number conversion/pointer constructors для Lua, implicit T*↔void* cast, automatic native lifetime ownership. Host registries не хранят gameplay components или managed refs внутри ECS struct. Pointer type ID не выдаёт права без host opt-in.
- Depends on: WP-02, WP-03, WP-04
- Acceptance: AC1, AC2, AC3, AC4, AC9, AC10.
- Verification: default deny/allow, unknown type ID, nil assignment, incompatible type/domain/generation/Simulation, revoked token в live view и snapshot, source native pointer host-lifetime contract. Snapshot copies address token only; Lua tostring/error не раскрывают raw address. Native тесты выделяют/освобождают память вне measured path, никогда не разыменовывают revoked tokens. Common successful validation/read/write 0 B.

### WP-06 — Integration, evidence и documentation

- Status: completed
- Goal: Принять весь feature range по AC1–AC10 и обновить достоверные текущие ограничения.
- Scope: Реальный mod/Runner flow Static/Dynamic × Deterministic/Parallel; both error policies, mixed native/dynamic schemas, copy_to и domain revocation, recreate scopes/weak refs. Canonical performance матрица, previous gates и targeted full suites. Обновить plans/lua-ecs-type-catalog-spec.md, общий script-modding status и docs/02_ADR/lua-ecs-common-bindings.md по факту окончательной реализации; no automatic knowledge notes. Prepared PR title/body отмечают dependent base, compatibility и unresolved platform checks.
- Boundaries: Не объявлять NativeAOT, editor hot reload/state restore или network/save поддержанными по этим tests. Не расширять unrelated cooked-assets/content/editor feature scope.
- Depends on: WP-01, WP-02, WP-03, WP-04, WP-05
- Acceptance: AC1, AC2, AC3, AC4, AC5, AC6, AC7, AC8, AC9, AC10.
- Verification: Все команды ниже на свежих сборках; independent read-only review subagent после integration, исправить actionable findings и повторить относящиеся checks. Review включает stale views, lifetime, pointers, exact conversions и allocation test negative controls. Предпочесть один необходимый negative control критического guard теста, не повторять все прошедшие suites без причины.

## Milestones

M0: isolation/baseline WP-00. M1: scalar+enum WP-01. M2: exact numbers WP-02. M3: nested/fixed WP-03. M4: snapshots WP-04. M5: pointer opt-in WP-05. M6: integrated acceptance WP-06. Каждый milestone закрывается только при записанном фактическом PASS по package verification. WP completion не означает весь feature complete.

## Concrete Steps

Cwd всех repository commands: /home/artem/git/Karpik.Engine. Перед запуском установить DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli, DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1, DOTNET_CLI_TELEMETRY_OPTOUT=1. Если SDK restore needs external network — записать конкретный failure, использовать имеющийся cache; не считать compilation failure behavioral Red.

Используется текущая codex/modding-0.7 с явного разрешения разработчика, task base4767dca. Branch switching/new dependent branch не выполнять. Перед продолжением проверить git status; чужой untracked IDE файл сохранить.

Baseline/affected build:

dotnet build tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false

dotnet build tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -c Release -m:1 -nr:false

Targeted Runner behavior baseline:

dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~TypedComponentBindingsTests|FullyQualifiedName~LuaTypedBindingsTests|FullyQualifiedName~DynamicComponentStorageTests|FullyQualifiedName~MixedComponentQueryTests'

Generator baseline/targeted:

dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter FullyQualifiedName~ClrComponentBindingGeneratorTests

Новые package tests использовать фильтром по добавленным именам классов, записывая точную команду и число выполненных tests; zero matches не PASS. После изменения source сначала свежая соответствующая сборка, затем допустим no-build.

Allocation matrix:

dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~Allocation'

Full affected acceptance:

dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-restore -m:1 -nr:false

dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -c Release --no-restore -m:1 -nr:false

Если реально меняется engine scheduling/public access identity вне binding слоя, дополнительно запустить tests/ECS.Core.Tests/ECS.Core.Tests.csproj; иначе не расширять тестирование на unrelated проекты без evidence. git diff --check и diff relative agreed parent обязательны.

Expected: exit 0, failed=0, ненулевые matched tests, no unexplained source diagnostics/regressions; common 0 B после подготовки. Evidence не подменять числом historical tests.

## Validation and Acceptance

Каждый AC связан с WP выше. End-to-end matrix проверяет public/generated/actual Lua flows, не только hand-built accessors. Exact values проверяются CLR oracle и boundary cases; decimal conversion использует rational точность, а не только приблизительное numeric equality. Schema parsing negative tests выполняются до системы. Disposal тесты показывают запрет доступа и возможность GC старых Simulation data; не удерживать source VM/world через snapshots после teardown.

Performance workloads: scalar copy L=1,O=0; mixed scalar copy всех kinds; nested/fixed copy с L=число scalar elements; exact-add L=1,O=1; retained snapshots плюс warmed copy_to; pointer read/write. N=0/1/16/256/4096. Не суммировать unrelated operations в O ради искусственного увеличения ceiling. Constructors/tostring и snapshot creation отдельно с bytes/call и retention. Common allocation counters охватывают полный prepared path; Lua данные отделяют matched direct VM стоимость. Новый ceiling действует только после утверждения плана. Писать p50/p95/p99, Gen0, preparation/growth и disposal/memory peaks; hard latency SLA для Lua не обещается.

Сохранить или расширить current F32 record constructor/init/with/Deconstruct regression checks. Новые kinds append-only; API compatibility проверяется компиляцией actual consumers/own+referenced generated provider tests. Если additive совместимость невозможна, не скрывать breaking change: сначала уточнить scoped migration contract.

После integration independent reviewer сравнивает diff с design revision 15/AC1–AC10, включая подключённые ownership места, но не объявляет собственные тесты выполненными без запуска. Главный агент исправляет найденные проблемы и делает relevant повторную проверку.

## Idempotence and Recovery

Tests/builds безопасно повторять при новых изменениях/ошибках. Документы являются живыми, completed package без evidence не ставить. Не reset/clean чужие файлы, не переписывать parent и не перезаписывать готовые snapshots/expected outputs ради тестов. Public/runtime failure оставляет соответствующий package незавершённым; rollback отдельного task change допустим только с сохранением пользовательских edits.

При превышении allocation gate оптимизировать wrapper/descriptor lifetime, не mutable exact values и не удаление guards. Если семантика, public contract или budget должны измениться — открыть только затронутый design вопрос и получить scoped решение. Если integration prerequisites влились в main — retarget законным способом и повторно проверить feature diff; inherited parent edits не должны входить в финальный feature PR. Не публиковать по неявному разрешению.

## Artifacts and Notes

Source design: /tmp/karpik-modding-types-design.md revision 15. После approved branch перенос в plans/modding-types-design.md; этот plan — plans/modding-types-execplan.md, исправить frontmatter source_design на modding-types-design.md. Не создавать вторые competing plans на WP.

Probe: /tmp/karpik-exact-probe/{Probe.csproj,Program.cs}. Измерение: N=0→816 B, 1→1344, 16→9264, 256→135984, 4096→2163504; 50 warmup и 50 measured calls, Release .NET 10.0.115/MoonSharp2.0.0. Это capability evidence, не runtime milestone.

Prepared future PR title: feat: Complete typed Lua ECS access and detached component snapshots.

Prepared body: «Lua bindings currently support only five scalar field kinds and reusable flat views. This extends typed common storage and generated native bindings to the remaining scalar kinds, exact integers/decimal, nested/fixed data and host-approved opaque pointers, with detached snapshots and reusable copy_to. Verification: [replace with actual commands/results]. Existing scalar contracts are preserved; new schemas/APIs are additive; Lua remains experimental with measured allocation budgets. Local task range starts at4767dca on explicitly authorized codex/modding-0.7; publication must isolate this feature scope and prerequisites before opening a PR. NativeAOT/editor reload/save/network are not proven by this change.» Apply actual repository feature labels at publication, explicitly identify any real breaking change; do not invent current label names.

## Execution authorization and adjustments — 2026-11-11

Явный ответ «Делай в этой ветке» утверждает implementation plan revision 1/design revision 15 и budget, с branch override: работа в текущей codex/modding-0.7 at 4767dca, без создания дочерней ветки. Все ранее proposed branch commands в этом документе отменены этим override. Документы перенесены в plans/. Current task diff отсчитывается от 4767dca. No push/PR остаётся как в plan. WP-00 isolation означает отделённый task diff/учёт, не отдельную ветку. Рутинные technical package splits не меняют behavior/ACs.

### Technical package decomposition

WP-02 exact Lua arithmetic module и WP-05 pointer policy registry выделены как независимые подготовительные подзадачи без edits общих файлов, до end-to-end зависимостей. Scalar common/codegen/Lua scalar source принадлежит одному agent; main начинает recursive schemas в новых файлах, shared edits только после handoff. Final package acceptance остаётся в dependency order. Repository builds/tests сериализует main; отдельные /tmp harness outputs изолированы.

### Focused integration evidence — 2026-11-11

Fresh Release Runner build: exit0,7 warnings (existing obsolete/generated-name/nullable warnings). Focused run /tmp/karpik-types-focused.trx:267 passed/1 failed/268 total. Единственный failure — старый LuaTypedBindings UnknownSchemaKinds test ожидал запрет i64; заменён на по-прежнему unsupported i128. Все новые ScalarCatalog24, Exact50, Snapshot8, Composite3, NativeComposite2, PointerPolicy8, LuaPointer7, LuaSchema6, LuaComposite25 и CompositeAllocation15 cases прошли. Существующие five-kind mixed allocations сохранились. Повтор/финальный полный run ещё предстоит.

No fabricated strict Red: scalar/common/native and isolated exact/pointer/parser missing-API Red наблюдались; snapshot и часть end-to-end tests были написаны до интеграции, но их отдельный Red из-за координации shared outputs не наблюдался. Это отмеченное отклонение от conventional auto-TDD, не заявка strict TDD. End-to-end acceptance дополнительно проверяется после реализации.

### Global run and guard refinement

Первый полный Runner run:577 passed/4 failed/581, exit1; все4 failures были одним новым test fixture after={fail}, нарушающим существующий full-ID contract. Fixture исправлен на catalog:fail. Actual Runner4 composition/scheduler cases прошли. Дополнительно inspection нашёл compact descriptor Get<T> мог принять неверный kind, если пользовательский accessor реализует два typed интерфейса. Новый ComponentDescriptorKindTests дал настоящий Red (No exception thrown, isolated existing DLL); исправлено явной kind/type проверкой без scalar boxing. Новый полный run будет включать этот regression test.

## Review findings and continuation — 2026-11-12

R1 [P2, fixed/reviewed] — native InlineArray мог быть молча принят как struct с одним backing field. Независимый reviewer указал root/nested paths; новый diagnostic test показал Red2/2 (старый generator принимает), исправление даёт KEB001 до обхода fields, isolated Green2/2. Focused независимое re-review закрыло R1, иных actionable defects не найдено. Reviewer ничего не менял и не запускал tests.

После kind guard fresh full Runner:581 passed/1 failed/582. Единственный failure existing mixed-five matched allocation N0:1984.008 против1984 B/callback,8B суммарно на1000вызовов; N0 не обращается к данным/новым field getters. Budget не изменён, требуется focused reproduction/recheck. Все новые runner matrix/policy cases и kind regression прошли. Первый Generator full run76/76 exit0, новый после InlineArray guards/regression pending.

### Matched gate reproduction — 2026-11-12

Focused rerun of unchanged existing LuaEcsAllocation_MixedFiveDoesNotExceedMatchedFiveFloatBaseline:5/5 passed, failed0,69s, /tmp/karpik-types-matched-recheck.trx. Все N0/1/16/256/4096 проверены; сравнение и пороги не изменялись. Единичные8B не воспроизвелись; этот результат не выдаётся за причину CLR accounting noise, только за nonpersistent failure в первом полном run. Финальный full Runner повторяется после InlineArray diagnostic rebuild.

### Constructor-injection invariant — 2026-11-12

R2 [project invariant, fixed/pending recheck]: self-audit и independent reviewer подтвердили, что optional fallback `new ComponentPointerRegistry` в двух production constructors нарушает запрет ручного создания зарегистрированного Simulation service. Constructor signatures совместимы, policy хранится nullable; legacy scalar-only callers не создают service, pointer schemas/bindings без policy явно запрещены. Injected registry остаётся borrowed/DI-owned, fallback owns/dispose удалены. Это wiring correction normal-mode, не изменение approved public behavior; существующий default-deny и новый native-query context test проверяют совместимость.

### Fresh full acceptance before DI ownership correction — 2026-11-12

Fresh post-InlineArray build passed. Full Runner582/582 exit0 (3m51s), Generator78/78 exit0 (8s), /tmp/karpik-types-{runner,generator}-accepted.trx. All previous gates unchanged, including matched-five N0. R2 source wiring correction was made after these binaries were built; it is not falsely included in582/582. Rebuild and final affected/default-deny verification after R2 still required. R2 focused independent re-review closed the project-invariant issue, no remaining findings.

### Final acceptance — 2026-11-12

После R2 fresh Runner build0errors/7existingwarnings; Generator dependency refresh0errors/1existingwarning. Финальный full Runner583/583 exit0 (4m7s), final Generator78/78 exit0 (10s). Test commands: dotnet test соответствующего project -c Release --no-build --no-restore -m:1 -nr:false --logger trx; DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli, NUGET_PACKAGES=/home/artem/.nuget/packages, VSTEST_CONNECTION_TIMEOUT=180. VSTest запускался вне sandbox из-за loopback socket restriction. Scope no-push/no-PR соблюдён. Все AC1–AC10 закрыты, standalone cold/factory/formatting/memory peak probe завершён по окончательным DLL, evidence сохранён. Source после окончательной сборки/прогонов не менялся; последующие изменения только docs/artifacts. git diff --check и ссылки docs проверены.
