---
status: completed
complexity: complex
complexity_source: auto
development_mode: strict-tdd
source_design: mod-dependencies-design.md
design_revision: 4
plan_revision: 1
approved_plan_revision: 1
approved_design_revision: 4
---

# Разрешать зависимости модов перед загрузкой и обновлением

Living ExecPlan по [PLANS.md](PLANS.md). [Дизайн r4](mod-dependencies-design.md) — источник D1–D9 и AC1–AC6.

## Purpose / Big Picture

Автор описывает зависимости в `mod_info.json`; хост выбирает один порядок для Lua и cooked Content до первого потребителя. Lua видит факт разрешения только собственных прямых зависимостей через `has_dependency(id)`. Некорректный startup не запускает часть ModSet, а невалидный refresh не заменяет активные ресурсы. Демо, полный ModSet reload, save/network, NativeAOT запуска игры — вне среза.

## Progress

- [x] (2026-11-23) Отдельная `codex/mod-dependencies` от `codex/modding-0.7` (`be5ddad`) с разрешения разработчика, готовый design r4. Проверены cold paths в исходниках; SDK 10.0.115 доступен. Код и тесты не менялись и не запускались.
- [x] (2026-11-23) Разработчик ответил «Делай» на запрос утверждения ExecPlan r1; утверждён дизайн r4, режим strict-tdd. Начат WP-00.
- [x] (2026-11-24) WP-00–WP-05 выполнены; Red → Green → Refactor evidence и сквозная проверка записаны ниже.
- [x] (2026-11-24) Независимое ревью и повторная проверка R1–R4 завершены.
- [x] (2026-11-24) Опубликован [PR #124](https://github.com/Karpiklalloly/Karpik.Engine/pull/124) в `codex/modding-0.7`, без слияния.

## Surprises & Discoveries

- Сам захват cooked-пакета не доказывает допуск: `ContentComposition.Build` startup отдельно может его пропустить при коллизии собственного AssetId либо бюджете. Проверка зависимости после `CompleteStartup` была бы поздней.
- Lua startup и code reload сейчас делят список, который повторно сортируется по ID. Refresh собирает отдельный `SortedDictionary` и может удержать предыдущий пакет после reclassification. Нельзя вычислять метаданные удержанного пакета по нынешнему `mod_info.json` или менять его metadata в месте reuse.
- `Runner.CompleteContentStartupAsync` выходит, когда `ContentRegistry` не зарегистрирован. Lua-only startup обязан пройти через тот же resolver, не полагаясь исключительно на этот callback.

## Decision Log

- 2026-11-23 / разработчик: strict-tdd, demo после общего плана; отдельная ветка на `be5ddad`, PR в родителя. Версии числовые; цель должна предоставлять активный payload; `has_dependency(id)` frozen; любые дубликаты ID фатальны. Refresh проверяет новый граф поверх замороженного Lua.
- 2026-11-23 / planning: отдельные resolver, startup, Lua и refresh work packages. План не закрепляет исполнителей/волны и не разрешает implementation до утверждения.

## Outcomes & Retrospective

AC1–AC6 подтверждены: обязательные/опциональные зависимости, проверка версии и стороны, единый cold-порядок, frozen `has_dependency`, публикация Content вместе с метаданными и отказ без частичной смены. Итоговый Linux Release: Runner 914/914 (включая 4 сквозных случая), Content.Runtime 173/173, Generator 80/80, **1167/1167**, без пропусков. Последняя правка R4 затрагивала только Lua parser; после неё targeted 334/334 и полный Runner повторены. Независимая проверка и перечек закрыли R1–R4. Демо, Windows execution и NativeAOT игры не проверялись. [Ownership ADR](../docs/02_ADR/content-runtime-snapshots.md#mod-overrides) и [справочник](../docs/modules/modding/mod-dependencies.md) обновлены. Публикация — отдельный PR в `codex/modding-0.7`, без merge.

## Context and Orientation

Рабочий каталог `/home/artem/git/Karpik.Engine`, ветка `codex/mod-dependencies`. Не править несвязанные untracked файлы/соседние worktrees. `Modules/Shared/Modding/Modding.Lua/ModsRegistry.cs` читает discovery; `LuaModSetPreparation.cs` захватывает пакеты и создаёт refresh callback. `src/Karpik.Engine.Core.Runner/Runner.ContentReload.cs` финализирует Content до Simulation. `src/Karpik.Content.Runtime/ContentRegistry.cs` и `ContentComposition.cs` владеют принятыми пакетами и первой публикацией. `LuaSimulationPreparation`, `ModsLifecycleManager`, `LuaScriptLoader` используют последовательность ModDefinition для первичной/повторной загрузки; `LuaModContentRefresh` строит cold-кандидат. ModSet — набор уже допущенных определений на стороне; payload — применимый Lua entry point либо cooked-пакет.

## Real-Time Assessment

Парсинг, сравнение версий, сортировка и проверки полностью cold. Индексы/массивы и наборы API готовятся заранее; `has_dependency` использует только pinned data, без чтения, ожидания или новой per-call аллокации bridge. Не менять frame, tick, ECS pool, scheduler, slot lookup, `TryCommit` и сетевые пути. Новый version/metadata-only package при необходимости делит store с прежним пакетом, claims остаются у существующей цепочки. Shared не импортирует Client/Server; Content не импортирует Modding. В WP-05 проверить unchanged paths, отсутствие нового прохода на публикации и warmed случаи.

## Plan of Work / Milestones

### WP-00 — Проверить рабочий baseline
- Status: completed. Release на `be5ddad`, SDK 10.0.115, submodules без расхождения: targeted Runner 88/88 и Content.Runtime 173/173, 0 пропусков; `-p:NuGetAudit=false`, serial и CI=true для Runner. Старый тест дубликатов подтверждает прежний fallback, его ожидание намеренно изменится в WP-02. Depends on: none. Acceptance: база AC6.
- Сверить task base и версии SDK/submodules. Запустить существующие discovery, host/reload и Runtime-базовые тесты по Concrete Steps. Убедиться, что testhost исполняет ненулевую выборку; при инфраструктурной проблеме восстановить рабочий запуск прежде Red. Зафиксировать текущие дубликаты и no-mod behavior, не менять production.

### WP-01 — Декларации и чистое разрешение порядка
- Status: completed. Depends on: WP-00. Acceptance: AC1, AC2, AC3 (core).
- Исполнитель `3628f77b-aac0-4bd3-a88f-9273ea5b7b97`; основной агент проверил исходники/whitespace и подтвердил 67/67 Release на тех же бинарниках (`--no-build --no-restore --filter FullyQualifiedName~ModDependenc`, 0 пропусков). Red → Green → Refactor, все через CI=true Release Runner: `RejectsInvalidTokenShapes*`/`RejectsDuplicateTargets*`/`PreservesExactIds*` — пустая модель или нет отказа, 20 Red → 23/23 → 23/23; `ReadsNumericBounds*`/`RejectsNonNumeric*`/`RejectsReversed*`/`RejectsExclusiveMaximum*` — неверный разбор/диагностика диапазона, 18 Red → 41/41 → 41/41; `ResolvesSharedAndCurrentSide*`/`RequiredDeclarationFails*`/`OptionalTargetWithoutSidePayload*`/`RejectedCookedOnlyTarget*`/`SeededDags*` и ordinal/duplicates/provider/cancellation — пустой результат или нет отказа, 13 Red → 54/54 → 54/54; `EnforcesBothActiveNumericBounds*`/`ConstrainedLegacyProvider*` — нет отказа несовместимой версии, 8 Red → 65/65 → 65/65; `PresentOptionalCycle*`/`SeededDagBackEdges*` — частичный результат вместо отказа, 2 Red → 67/67 → 67/67. Не относящиеся к поведению compile/test-fixture ошибки не засчитаны. Интеграция с discovery и публикацией ещё не проверена.
- Согласован внутренний стык: parser принимает `JToken?` (`null` CLR — отсутствие секции), resolver принимает узлы с отдельным Lua payload и cooked статусом Absent/AdmittedCandidate/Rejected; возвращает порядок, прямые цели и ID участвующих cooked-only узлов для проверки admission. Участие означает фактическое ребро, не отсутствующую optional цель. Проверку duplicate JSON properties в новом контракте делает WP-02 на границе reader; `JObject` по умолчанию уже теряет раннее значение.
- Область: Modding.Core/Modding.Lua cold-модель и разбор через существующий JSON path, числовые границы, активные Shared/side, выборка узлов с переданной eligibility, resolved direct IDs, стабильный топопорядок. Доступность за пределами чистого ядра в WP-02/04; пакет не владеет `ModsRegistry.cs`/Runner, не меняет Lua API или Content. Для интеграции зафиксировать один immutable вход (источник/сторона/статус Lua и Content/декларации) и выход (порядок, direct target sets, participating cooked providers). Не синтезировать второй граф для Lua.
- Проверки: тесты парсера/резолвера на точные границы, старые версии, группы и пересечения, non-active declarations, отсутствующую optional, неподходящую версию optional, самозависимость/цикл без downstream шума, независимые узлы. Seeded DAG с перестановками входа, проверка всех рёбер и одинаковой очереди, плюс циклическое преобразование; неподдержанный формат не падает только от missing implementation. Целевые `ModDependency*`.

### WP-02 — Startup: discovery, допуск и первая публикация
- Status: completed. Depends on: WP-01. Acceptance: AC1, AC2, AC3, AC6 (startup).
- Исполнитель `6cbe0962-2571-4335-84ff-338d1726b09f`; основной агент проверил diff и повторил targeted Release 33/33 (`ModDependencyStartupTests|ModDiscoveryTests`, `--no-build --no-restore`). Strict TDD: duplicate/fatal syntax/invalid graph/capture and composition rejection/linked provider/Lua-only/pre-publication cleanup и non-ordinal winner — 29 ожидаемых Red из 51 (не было отказа либо неправильные `(3,1)` вместо `(2,1)`); дополнительная проверка двух сочетаний dependency syntax/legacy warning — 2 Red. Начальный Green 47/51 выявил у положительной fixture отсутствие версии провайдера; после исправления 53/53. После refactor Release Runner targeted 232/232, Runtime 173/173, 0 пропусков. NativeAOT и не-Linux не запускались. Lua API и refresh вне границы.
- Согласован стык `ModsRegistry.StartupDependencies`: замороженные узлы и то, что разрешил граф, плюс упорядоченная Lua-проекция для setup/reload. Прежняя активация Lua-записи без подходящего entry point сохраняется, но такая запись не даёт payload для D4. Startup guard видит принятые Content-пакеты после композиции и до создания claims/первой публикации; Lua-only вызов не зависит от установки ContentRegistry.
- Область: `ModsRegistry.cs`, `LuaModSetPreparation.cs`, start-only seam `IModContentParticipant`/`ContentRegistry`/Runner. Отделить ошибки нового dependency-контракта и дубликаты от старых диагностик. Проверить entry point штатной файловой семантикой, разместить разрешение после холодного захвата применимых кандидатов, но до Lua VM. Подать Content порядок резолвера; перед первой публикацией сравнить реально принятые пакеты с графом. Сохранить очистку captures на всех отказах и Lua-only путь; не добавлять второй алгоритм Content admission. Заморозить startup Lua definitions, их порядок/eligibility и resolved direct targets для WP-03/04. Content Runtime остаётся backend-neutral.
- Проверки: cold startup missing/cycle/optional version/duplicates на реальных manifests; утверждённая intentional регрессия прежнего fallback дубликатов (сначала обновить ожидание и наблюдать Red). Наличие только противоположного side, ошибка capture и последующий отказ composition (включая совпавший AssetId), отсутствие ID публикации/VM и cleanup claims. Ordinal без рёбер, Startup ID v1 и победитель с ребром, no-Content host. Целевые `ModDiscoveryTests|ModContentHostTests|ModDependency*`; локальные Runtime startup tests для изменённого контракта.

### WP-03 — Порядок Lua lifecycle и has_dependency
- Status: completed. Depends on: WP-01, WP-02. Acceptance: AC3, AC4, AC6 (Lua).
- Исполнитель `36106fc1-a3e4-426b-9d91-e45004984350`; основной агент проверил scope/whitespace и повторил `ModDependencyLua` на тех же Release binaries: 24/24. Strict TDD: `StartsProviderBeforeConsumer`/`ResolvedLifecycleOrderSurvivesCodeReloadAfterManifestEdit` — 8 Red, consumer раньше провайдера → 10/10 → 10/10; `QueriesOnlyOwnResolvedDirectIdsFromSharedAndSide`/`UsesFrozenDirectIdsInReloadCandidate`/`UnresolvedDefinitionHasAnEmptyDependencySet`/`WarmedBridgeHitAndMissDoNotAllocate` — 6 Red, API отсутствовал → 16/16 → 16/16 после refactor; `RejectsWrongArgumentTypeOrArity` — 8 Red, не было Lua error → 24/24 → широкий recheck Runner 216/216, 0 пропусков. Два ordinal случая уже Green. Bridge (без MoonSharp dispatch/ошибок) 0 B на 20k прогретых hit/miss, `DynValue.True/False` переиспользуются. Реальные startup и reload Static/Dynamic × Client/Server, ECS state preserved после редактирования manifest. Constructor/deconstruction `ModDefinition` сохранены; добавлен immutable init-only set. Полная связка Lua+Content+refresh в WP-05.
- Область: ModDefinition или минимальный уже используемый immutable контракт, `LuaSimulationPreparation`, `LuaScriptLoader` и при необходимости lifecycle tests. Использовать готовый порядок без повторной сортировки, передать замороженные результаты в первичный и candidate runtime до топ-level. Ограничить global `has_dependency` своим direct set; scheduler и API других модов не менять. Тестировать текущие контракты для тестовых/кастомных registries, не вводить глобальное состояние.
- Проверки: real-file MoonSharp Shared/side top-level и `on_load`, dependency-first lifecycle, отсутствующая/незаявленная/транзитивная цель, неверные аргументы; code-only reload после изменения manifest на диске сохраняет разрешение и порядок (и ECS). Отдельно проверить новый callback на отсутствие дополнительной managed работы/аллокаций bridge; расходы MoonSharp не выдавать за zero-GC. Целевые `LuaPreparationTests|LuaHotReload|ModDependenc`.

### WP-04 — Content refresh поверх зафиксированного Lua
- Status: completed. Depends on: WP-01, WP-02. Acceptance: AC3, AC5, AC6 (refresh). Основной агент подтвердил полный Content.Runtime Release 173/173, 0 пропусков, после проверки финального refresh diff.
- Исполнитель `58fde69f-6da3-474d-8ba7-c568a8107e14` оставил реализацию и исполняемые Red/Green до остановки по quota `PI_AI_ERROR`, не по ошибке C# или тестов. A: `ResolvedOrderSelectsNonOrdinalWinner` ×4 и `ActiveScriptWithoutFrozenEntryUsesOnlyActualCookedSelection` ×2 — неверный winner или принятие неподходящего провайдера, 6 Red → 29/29 → 29/29. B: optional install/removal в `RejectsChangingLuaDirectDependency` и `RejectsChangingRelativeLuaOrderWithUnchangedDirectSets` — 3 Red (`Applied`) → 33/33 → 33/33; required removal уже Green. C: `PublishesMetadataOnlyAndRetainsItAcrossReclassification` ×2, `DeclarationOnlyUpdateReordersUnchangedCookedStores`, `CompositionFailureLeavesPriorPackageMetadataForRetainedLayer` — 4 Red (та же identity, прежний winner, прежняя версия в candidate) → 15/15 Green. Завершающий прогон исполнителя не засчитан; после cooldown основной агент проверил diff, перенял build ownership и подтвердил fresh targeted Runner 209/209 (включая 4 сквозных случая WP-05); Затем отдельно подтверждены Runtime 173/173 и полный заключительный прогон WP-05.
- Область: `LuaModContentRefresh.cs`, cold glue из `LuaModSetPreparation.cs`, при необходимости `ModContentCapture`/`ModContentPackage` для ассоциации immutable metadata с package identity. Из предыдущих committed packages и discovery собрать реальный кандидат по существующим D14 selection rules; content-only metadata следует за пакетом при retain/reuse; version/declaration-only update использует тот же store, но новое immutable состояние. Не писать в состояние активного пакета до commit; не регистрировать второй coordinator. Валидировать порядок и direct sets startup Lua до передачи захватов; из базы брать последний committed Content layer. Base reload не пересчитывает граф.
- Стык: coordinator уже передаёт `CommittedSnapshot.Packages`; `ContentRegistry` сравнивает список package identities отдельно от набора слотов. При изменении только Modding metadata новый package identity может переиспользовать store и slot refs, но проходит той же публикацией. Не снабжать предыдущий объект новыми metadata при таком reuse; cooked status startup snapshot для refresh не авторитетен. Сохранить boxed/VM hot path нетронутым.
- Проверки: два пакета с принудительным не-ordinal победителем; valid update с прежними cooked bytes, no-op, обязательную цель нельзя удалить, optional presence нельзя поменять для Lua, проверка относительного порядка без изменения direct sets, freeze metadata после изменения скриптовой папки, удержанная бывшая content-only папка и отказ/stop/cancel без утечки claims и метаданных. Следующий refresh реально видит сохранённый committed graph, не только результат текущего теста. Целевые `ModContentHostTests|ModDependenc`; Runtime пакетные/coordinator regressions. Lua API проверки этой матрицы интегрируются в WP-05.

### WP-05 — Интеграция, документация, независимое ревью
- Status: completed. Depends on: WP-02, WP-03, WP-04. Acceptance: AC1–AC6. Независимое ревью и перечек R1–R4 закрыты без открытых замечаний.
- 2026-11-24: полный serial Linux Release после интеграции, `-m:1 -nr:false -p:NuGetAudit=false`, Runner `CI=true` и blame watchdog: Runner 894/894, Content.Runtime 173/173, Generator 80/80; 1147/1147, 0 пропусков и exit 0 для каждого. Логи `tests/Karpik.Engine.Core.Runner.Tests/bin/mod-dependencies-final.RlHgXs`.
- `ModDependency_EndToEndKeepsLuaAndContentInOrderAcrossReloads` — 4/4 внутри fresh targeted 209/209; реальные `Mods` и cooked outputs, Client/Server × Static/Dynamic, startup видит прямую cooked зависимость, code-only reload использует frozen manifest и сохраняет entity, базовый reload меняет только legacy read, затем Content refresh принимает совместимый пакет и отклоняет removal с теми же refs/sequence/VM/world. Это финальная приёмка уже реализованных инкрементов, не новый TDD Red. Англоязычный packaging, Lua guide, dependency reference и ADR обновлены, ссылки и JSON примеры валидны.
- Прогнать Static/Dynamic × Client/Server, Lua-only, реальные manifests/скрипты/cooked packages; dependency-first и последний override, затем code-only reload, base reload и guarded Content refresh. Проверить same Lua/Simulation и stable refs/claims/notifications при отказе. Провести финальные Release checks, без повторного прогона неизменной матрицы в каждом WP. Описать `mod_info.json`, `has_dependency`, restart-only случаи и breaking duplicate change в `docs/modules/modding/` и `docs/mod-content-overrides.md`, уточнить ссылки/ownership в соответствующих ADR.
- После интеграции отдельный read-only reviewer сверяет D1–D9, AC1–AC6, transactional failure/abort/reuse, lifecycle and side parity, backwards behavior с исключением D6, отсутствие hot-path и scope drift. Основной агент triages находки и повторяет затронутые проверки; strict TDD применяется к новым fixes. Проверить scope diff и оформить отдельный PR по фактическим результатам. Без демки, слияния в `main` и неподтверждённых заявлений NativeAOT.

## Review findings (WP-05)

- R1 [major, closed] — Explicit bad dependency contract could be masked by trailing JSON or pre-token syntax errors (AC2). Trailing ×2: missing exception → validation-before-tail → Discovery 35/35. `Discovery_RejectsMalformedRootDependencyKeyBeforeReaderEmitsIt` ×5 and valid-dependencies/legacy syntax control ×1: no fatal or false fatal → 47/47 combined with R2. Duplicate followed by malformed sibling: 1 Red → 48/48. `Discovery_DoesNotDowngradeExplicitDependencyErrorsToLegacyWarnings` fully parsed invalid contract before malformed sibling ×3: no fatal → 51/51, post-refactor/no-op 71/71 Discovery+Startup. Exact decoded root key detected via Newtonsoft 13.0.4 name table before colon; fallback bounded replay of already captured bytes only after a later unrelated parser failure, no disk reread. Legacy warnings retain unrelated behavior.
- R2 [major, closed] — Duplicate-check full JSON paths amplified cold memory (AC2/AC6). `Discovery_DoesNotExpandLongAncestorNamesForEachProperty` ×2 measured 13.14–13.21 MB where limit was 4 MiB; object-local duplicate keys → Green 47/47 with R1, wider post-refactor 175/175 (excluded separate R3 Red). Generate full path only on duplicate inside the new section, not for valid legacy properties.
- R3 [medium, closed] — `GetMods()` and Simulation exposed different resolved definitions (AC3/AC4). `ModDependency_EndToEndKeepsLuaAndContentInOrderAcrossReloads` ×4 actual Red: public definitions returned null sets; single enriched startup projection → 4/4 Release. Refactor removes second enrichment in Simulation; targeted recheck 333/333.
- R4 [medium, closed] — Valid declarations eagerly formatted the source mod ID per item, amplifying cold allocation. `ParsesLongSourceIdWithoutAllocatingDiagnosticTextForEveryDeclaration`: 12,896,112 B Red (4 MiB limit) → format only on error, targeted 334/334 Green. No further refactor required; full Runner recheck 914/914 (`tests/Karpik.Engine.Core.Runner.Tests/bin/mod-dependencies-final-review.hvjbaq`).
- Independent reviewer `420eea22-d14e-44af-b7f6-184683493288` checked the integrated diff without edits/tests, confirmed R1–R3 closed on re-review, then reported and independently closed R4. Custom participant replacement caveat documented in Content ADR; stock wiring unchanged. `AbortedMetadataOnlyPreparationKeepsTheCommittedVersionAndStore` passed with unchanged package identity, store and retry against old metadata; no production change for already-valid abort behavior.
- After R1–R3: targeted 333/333, full Runner 913/913, Content.Runtime 173/173, Generator 80/80, zero skipped; logs `tests/Karpik.Engine.Core.Runner.Tests/bin/mod-dependencies-review.273oh4`. R4 touches only dependency parser and one Runner test.

## Concrete Steps

Из `/home/artem/git/Karpik.Engine`; `CI=true` для Runner, сборка single-node, node reuse off. Targeted filters уточняются по созданным тестам с подтверждением ненулевой выборки:

```sh
CI=true dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false --filter 'FullyQualifiedName~ModDiscoveryTests|FullyQualifiedName~ModContentHostTests|FullyQualifiedName~LuaPreparationTests|FullyQualifiedName~LuaHotReloadIntegrationTests' --blame-hang --blame-hang-timeout 180s --blame-hang-dump-type none
dotnet test tests/Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -c Release -m:1 -nr:false
CI=true dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false --blame-hang --blame-hang-timeout 180s --blame-hang-dump-type none
dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -c Release -m:1 -nr:false
git diff --check
```

Первые две проверки — baseline; последние наборы с новыми тестами — финальная приёмка. Выделить build outputs либо сериализовать общие каталоги. Для каждого проверяемого изменения сначала исполняемый тест с наблюдаемым поведенческим Red, затем минимальное поведение и Green, потом Refactor/recheck (или запись no-op). Compile/restore/testhost errors не считать Red. Для нужной публичной сигнатуры допустима нейтральная заглушка до Red. Записывать test ID, причину Red и counts Green/recheck; self-check docs напрямую.

## Validation and Acceptance

Все AC перечислены в [дизайне r4](mod-dependencies-design.md#acceptance-criteria), локальные покрытия указаны в WP. Успех: полный Release без необъяснённых failures, независимое ревью без открытых actionable findings, подтверждённые исходный надёжный startup и отказ без частичной публикации. Документировать известные непроверенные среды и не делать выводов по демке, NativeAOT, сети или сохранениям.

## Idempotence and Recovery

Не сбрасывать рабочее дерево. Invalid startup не публикует ModSet для потребителей и закрывает captures; invalid/cancelled refresh оставляет active packages, не фиксирует tentative metadata, после settlement допускается retry. Base reload работает с последним принятым слоем. При откате — обычный revert PR полным набором. Если родитель не опубликован/изменён, не обновлять его без разрешения; подтвердить чистый task diff и сообщить точный blocker (и подготовленный PR body/title).

## Artifacts and Notes

Design: [mod-dependencies-design.md](mod-dependencies-design.md). PR target `codex/modding-0.7`; retarget на `main` только если зависимый PR ещё открыт после merge родителя и проверен его diff. Expected title: `feat: Resolve versioned mod dependencies by side`. Отдельный demo task не создаётся здесь. Adjustments: none.
