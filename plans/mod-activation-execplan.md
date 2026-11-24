---
status: executing
complexity: medium
complexity_source: auto
development_mode: tdd
source_design: mod-activation-design.md
design_revision: 2
plan_revision: 1
approved_plan_revision: 1
approved_design_revision: 2
---

# Включать и отключать моды через пользовательский `mods.json`

Living ExecPlan по [PLANS.md](PLANS.md). Источник контракта: [дизайн r2](mod-activation-design.md), D1–D5 и AC1–AC4.

## Purpose / Big Picture

Игрок или оператор кладёт `{"disabled":["weather-ui"]}` в `mods.json` рядом с `Mods/` и запускает игру. Lua и Content этого мода не загружаются. Удаление ID и новый запуск возвращают мод. Пакет и его `mod_info.json` не переписываются.

## Progress

- [x] (2026-11-24) PR #124 squash-merged в `codex/modding-0.7` (`872b052`); отдельная `codex/mod-activation` создана от этого коммита по явному разрешению. Дизайн r2 сохранён; production, тесты и PR данной задачи ещё не начаты.
- [x] (2026-11-24) Разработчик утвердил ExecPlan r1 и дизайн r2: «Делай».
- [x] T1 — startup policy и discovery: исходный Release Red 14/86 (конфиг игнорировался, disabled manifest/граф валидировались); после реализации и подключения тестового logger все 86 startup/discovery tests прошли, отдельно оставался ожидаемый Red refresh.
- [x] T2 — неизменяемый выбор при Content refresh: в Red выключенный пакет вернулся и сменил override `(3, 2)` на `(9, 2)`; Green — 228/228 Release без пропусков (startup, discovery, dependencies и refresh).
- [x] T3 — сквозная проверка и документация: финальный Linux Release Runner 938/938 и Content.Runtime 173/173, без пропусков (1111/1111); файл-пример и ссылки проверены.
- [ ] Опубликовать PR в `codex/modding-0.7`.

## Surprises & Discoveries

- Startup и Content refresh используют discovery разными входами. Фильтр только на старте позволит выключенному content-only пакету вернуться через refresh.
- `ModsRegistry.ReadMetadata` объединял определение ID и валидацию manifest. Выключенный ID отсеивается после проверки синтаксиса, до семантических проверок; прежняя приоритетность diagnostics сохранена. Дубликаты проверяются отдельно от списка активных владельцев. Legacy binding при вариантах регистра ключа, повторах и числовом ID покрыт отдельно.
- Первый полный Runner остановил testhost по watchdog на новом e2e: асинхронное чтение перед pump превратило xUnit continuation в не-владеющий поток для `MainThreadScheduler`. Перенос первого await после pump (как в существующем dependency e2e) восстановил целевой прогон: 24/24. Правка только тестовая.

## Decision Log

- 2026-11-24 / разработчик: отдельный `mods.json`; отключённый распознаваемый ID освобождён от семантической проверки пакета, но не от проверки дубликатов; PR в `codex/modding-0.7`.
- 2026-11-24 / planning: medium auto, один исполнитель; проект уже имеет нужные end-to-end fixtures. Автоматический режим — TDD для поведения, прямые проверки для документации. Strict TDD предыдущего PR относился к задаче зависимостей.

## Outcomes & Retrospective

AC1–AC4 подтверждены: startup отсекает отключённые декларации, Lua и cooked Content; duplicate ID и невалидный `mods.json` остаются ошибками. Client/Server × Static/Dynamic, Lua-only и все три reload-пути покрыты. Финальный Linux Release: Runner 938/938, Content.Runtime 173/173, без пропусков. Документация: [включение модов](../docs/modules/modding/mod-activation.md), [Content snapshot ADR](../docs/02_ADR/content-runtime-snapshots.md#mod-overrides). Windows, NativeAOT игры и демо не запускались.

## Context and Orientation

`Modules/Shared/Modding/Modding.Lua/ModsRegistry.cs` читает `Mods/` из корня IFileSystem. `LuaModSetPreparation.cs` использует результаты discovery для cold dependency resolution и startup Content до Lua; передаёт список владельцев для диагностики подмен. `LuaModContentRefresh.cs` заново обнаруживает кандидатов, сохраняя startup Lua. ModSet — один набор модов, выбранный при подготовке хоста. `src/Karpik.Content.Runtime/ContentComposition.cs` уже пропускает override на отсутствующего владельца. `tests/Karpik.Engine.Core.Runner.Tests/ModDiscoveryTests.cs`, `ModDependencyStartupTests.cs`, `ModDependencyRefreshTests.cs` и `ModDependencyEndToEndTests.cs` содержат test seams для реальных файлов, cooked пакетов и side/static matrix.

## Real-Time Assessment

Загрузка bounded JSON и ordinal set lookup идут только при старте или cold discovery для refresh; refresh не читает сам `mods.json`. ModSet владеет неизменяемым выбором, который переиспользуется; ни I/O, ни lock, ни lookup не добавляются в frame/tick/ECS, Lua dispatch, network или Content slot reads/publication. Граница Shared/Client/Server, fixed dt и владение ресурсами не меняются. Не нужен отдельный benchmark без hot-path изменений; контроль — scope diff и тесты startup/refresh.

## Plan of Work / Milestones

### T1 — Читать выбор при старте и фильтровать discovery

- Status: completed. Acceptance: AC1, AC2, AC3 (startup). Depends on: none.
- В `ModsRegistry.Scan` выбрать конфиг один раз из корня рядом с `Mods/`. Ограничить размер чтения обычного файла; отсутствие — пустой выбор, а ошибка чтения/структуры — явный отказ с путём. Использовать имеющиеся .NET API и безопасный файловый helper, если тот сохраняет stock platform support и отличает отсутствие от неверного пути. Не вводить публичный API или новый пакет.
- В `ModsRegistry.DiscoverAt` исключать выключенный ID после получения распознаваемой идентичности из полного читаемого JSON и до семантических проверок `dependencies`/payload. При неизменном или включённом выборе полностью сохранить текущие ошибки/диагностику. Изолировать ordinal duplicate check по идентичностям; disabled не попадает в Lua, в узлы резолвера, в capture или в владельцев Content composition. Сохранить существующее публичное создание реестра.
- Test-first в `ModDiscoveryTests.cs` и startup fixtures: отсутствующий/пустой и неверный конфиг, root, точность ID/повторы/неизвестные, выключенный семантически сломанный manifest и payload, malformed JSON в прежнем режиме, дубликаты у выключенных, влияние обязательной и optional цели, отсутствие cooked и override. Ожидаем понятные Red и затем зелёные discovery/startup/relevant Content checks.

### T2 — Переиспользовать выбор во всех refresh того же ModSet

- Status: completed. Acceptance: AC3, AC4. Depends on: T1.
- Передать в `LuaModContentRefresh` из `LuaModSetPreparation` тот же выбор, что и в startup discovery, и применить при повторном `DiscoverAt`. Не добавлять watchers и не менять Content.Runtime, Lua code reload и base reload. Сохранить нынешние reclassification, genuine removal и transactional failure/cleanup rules для включённых модов.
- Test-first в Runner host fixtures: после старта изменить либо испортить файл; refresh продолжает обновлять доступный активный пакет, но не добавляет выключенный и не удаляет включённый из-за файла. Сохранить диагностику override на недоступную цель; проверить повторный refresh и применение обратного выбора новым хостом.

### T3 — Сквозная приёмка и публикация

- Status: in_progress. Acceptance: AC1–AC4. Depends on: T1, T2.
- Реальные Lua и cooked пакеты, Client/Server × Static/Dynamic через Runner fixture; Lua-only — отдельный целевой startup case. Покрыть взаимное согласование VM, `GetMods()`, `has_dependency`, Content, config failure до публикации, обе смены выбора после рестарта и невосприимчивость трёх reload-путей. Не дублировать матрицу там, где она ничего не доказывает дополнительно.
- Описать `mods.json`, restart-only и diagnostics в `docs/modules/modding/`; связать с `docs/modules/modding/mod-dependencies.md`, `docs/mod-content-overrides.md`, при необходимости уточнить ownership в `docs/02_ADR/content-runtime-snapshots.md`. Проверить docs примеры непосредственно, без искусственного TDD для текста. Проверить итоговый scope, собрать доказательства, push отдельной ветки и открыть PR в `codex/modding-0.7`. Не сливать этот PR или родительский без нового запроса.

## Concrete Steps

Рабочая директория `/home/artem/git/Karpik.Engine`, SDK по `global.json`; выполнить из ветки задачи. Start with T1/T2 filters and widen at T3. Для Runner `CI=true`, single-node/no node reuse:

```sh
CI=true dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false --filter 'FullyQualifiedName~ModActivation|FullyQualifiedName~ModDiscoveryTests|FullyQualifiedName~ModDependency' --blame-hang --blame-hang-timeout 180s --blame-hang-dump-type none
CI=true dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false --blame-hang --blame-hang-timeout 180s --blame-hang-dump-type none
dotnet test tests/Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -c Release -m:1 -nr:false
git diff --check
```

При инфраструктурной проблеме сначала подтвердить ненулевой baseline и записать причину/команду; restore/compile/fixture error не считать Red. Сохранять конкретные тесты и counts по этапам. `-p:NuGetAudit=false` допустим лишь при проблеме доступа к advisory feed, с записью причины.

## Validation and Acceptance

Таблица задач выше покрывает AC1–AC4. Итоговые suites в Release без необъяснённых failures/skip, подтверждённые Client/Server и Lua-only cases, отсутствие diff в hot paths. Отдельно зафиксировать непроверенные ОС и NativeAOT; демо не входит в задачу. PR title: `feat: Enable or disable mods at startup`; описание объясняет формат, совместимость, проверки и ограничение по рестарту.

## Idempotence and Recovery

Некорректный startup не публикует ModSet; исправить или удалить `mods.json` и перезапустить. Runtime изменений этого файла не читает; при неудачном обычном refresh остаётся прежний каталог. Если нужно откатить фичу — revert отдельного PR; файлы пакетов не мигрируют. Не удалять несвязанные untracked файлы и не обновлять `main`.

## Artifacts and Notes

[Дизайн r2](mod-activation-design.md). Проверить, что dependent diff относится только к активации, и base остаётся `codex/modding-0.7`. Adjustments: none.
