---
status: completed
complexity: complex
complexity_source: auto
development_mode: strict-tdd
source_design: asset-overrides-design.md
design_revision: 16
plan_revision: 4
approved_plan_revision: 2
approved_design_revision: 16
---

# Подключать и обновлять подмены ассетов модов

This living ExecPlan follows [PLANS.md](PLANS.md). The authoritative behavior is [design r16](asset-overrides-design.md), D1–D14, AC1–AC9.

## Purpose / Big Picture

Игрок копирует готовый мод в `Mods`. Игра загружает выбранные подмены по прежним `AssetId`; штатный reload базы их сохраняет. Явный локальный запрос позволяет обновить контент и добавить/удалить content-only пакеты без смены Lua/Simulation. Обработка путей старых загрузчиков, формат version-bound refs, CLI commands и существующий reload transport сохраняются. Новые UI, IPC, polling и Lua ModSet reload вне задачи.

## Progress

- [x] (2026-11-19) Проверены ранние потребители, публикация, store/retirement и возможности CLI по исходникам; сохранён ready design r16.
- [x] WP-00 (2026-11-20, `03f2ead`): SDK 10.0.115, submodules на ожидаемых коммитах; Release `dotnet test` из Concrete Steps: Core 188/188, Runtime 98/98, Runner 702/702, Generator 80/80; ноль пропусков. Верифицированы порядок Engine → ModSet preparation → Simulation, ранний OpenGL `Start`, отдельная активация endpoint и обратный порядок освобождения. `git diff --check` прошёл; NuGet audit сообщил о read-only HTTP cache (NU1900, тесты не блокировал).
- [x] Выполнены WP-01–WP-05, наблюдаемые Red → Green → Refactor записаны; независимое ревью и два перечека закрыли R1–R9.
- [x] (2026-11-21) Итоговый Release: Core 188, Runtime 173, Transport 38, Tool.Reload 26, Runner 753, Generator 80 — 1258/1258, 0 пропусков; OpenGL `--content` и `--mod-refresh` прошли.
- [ ] PR не опубликован: `origin/codex/modding-0.7` на `4767dcad`, 5 локальных родительских коммитов до `03f2ead` отсутствуют на GitHub; не обновлять родителя без разрешения.
- [x] (2026-11-21) WP-04 и WP-05A возобновлены после лимита модели; частичные изменения сохранены, пакеты не приняты до подтверждения проверок. Для [руководства автора](../docs/mod-content-overrides.md) реально прогнаны `content create`/`content build --target Client` на PNG; проверены marker, тип `texture` и точные байты артефакта. Раздел API refresh дописан и указывает на публичный `IModContentRefresh`.

## Surprises & Discoveries

- OpenGL читает базовые шейдеры через legacy API до подготовки ModSet; целиком откладывать Content нельзя.
- Текущий no-op определяет равенство по ID-слотам. Изменение скрытого оригинала требует сменить базовый индекс и удержание store без ложного события по ID.
- Claim в директории автора защищает от штатного сборщика поколений, но не от ручного удаления. Захваченные копии должны переживать исходную папку и ModSet scope.
- В текущем окружении `glxinfo -B` на `:0` даёт OpenGL 4.5 Mesa llvmpipe (Accelerated: no), `/dev/dri` отсутствует. После WP-01/02 Release-сборка в изолированных artifacts и базовый нативный `--content` с `SDL_VIDEODRIVER=x11 LIBGL_ALWAYS_SOFTWARE=1` прошли: submit/readback old green, retire, submit/readback new red, shutdown. Это досрочная квалификация программного GL; модовая подмена на нём и аппаратный GPU пока не проверены.
- Static generator отклоняет примитивный `bool` в конструкторе экспортируемого сервиса, даже с default. Для deferred startup используем optional reference marker, внедряемый Runner до Engine Build; прямые вызовы и hosts без marker сохраняют обычное поведение. Проверить реальной Static composition в WP-04.
- WP-01 воспроизвёл ограниченным по времени тестом блокировку открытия FIFO на Linux: managed `FileAttributes` не различает обычный файл и FIFO. Проверка mod capture должна отказать до открытия; целевая матрица -- Linux/Windows (`content-generation-ownership.yml`), базовый путь не ограничивается.

## Decision Log

- 2026-11-19 / разработчик: `codex/asset-overrides` зависит от `codex/modding-0.7` (`03f2ead`); PR первоначально в родителя, затем retarget и проверка diff после его merge.
- 2026-11-19 / planning: complex из-за границы startup, двух видов публикации и владения файлами/GPU. Исполнение само выбирает делегацию и порядок среди независимых пакетов; единственный автор лога плана — основной агент.
- 2026-11-20 / разработчик: явный `strict-tdd` вместо авто-TDD во всех пакетах; архитектура, зависимости и AC не меняются.

- 2026-11-20 / разработчик: явно утверждён ExecPlan r2 по дизайну r16.

## Outcomes & Retrospective

Реализованы AC1–AC9. Итоговые 1258 Release тестов прошли; один пограничный исходный Lua benchmark не прошёл первый общий повтор, затем прошёл 5/5 отдельно и 753/753 вместе с Runner на тех же бинарниках. Клиентский OpenGL probe подтвердил cooked texture, readback и retirement на llvmpipe; аппаратный GPU и Windows не запускались. Независимое ревью закрыто без actionable findings. Документы: [ADR владения](../docs/02_ADR/content-runtime-snapshots.md), [руководство автора](../docs/mod-content-overrides.md). PR не открыт из-за несовпадения удалённого основания, см. Prepared PR.

## Context and Orientation

Workspace `/home/artem/git/Karpik.Engine`, задача на `codex/asset-overrides`, не править parent или соседний worktree. Поколение — опубликованная CLI версия файлов пакета; захват — отдельная локальная копия этих файлов с управляемым временем жизни; root — атомарно опубликованное представление контента. `ContentRegistry.cs`/`ContentSlot.cs` владеют root и store; `ContentReloadCoordinator.cs` готовит кандидата и публикует на кадре; в Runner `Runner.cs`, `ContentReloadHost.cs` и `ContentReloadHostInstaller.cs` управляют регистрациями и boundary. `ModsRegistry.cs` и `LuaModSetPreparation.cs` — стартовый каталог, `ModMetaData.cs` — метаданные; `IEngineRunner.cs`, `Bootstrap.cs` — host API. Поля манифеста и проверка поколения уже описаны в `src/Karpik.Content.Core/ContentManifest.cs` и `ContentGeneration.cs`. Исполнение начинает с проверки точных контрактов и текущих зависимостей.

## Real-Time Assessment

Сканирование, JSON, копирование/хеширование, граф зависимостей и staging выполняются вне тика/рендера; объём ограничен D12. Подмена по ID не добавляет второй lookup, GC allocation, lock или I/O в `TryGet`/resolve. В boundary — готовый root и существующий bounded changes buffer, без декодирования, ожидания или публикации callbacks. Базовый индекс обновляется атомарно с root. Не менять ECS/layout, dt, scheduler Lua, протокол или направление Client/Server зависимостей. Верифицировать warmed reads/commit, длительность подготовки и дисковый пик отдельно, repeated refresh и renderer retirement по существующему контракту.

## Plan of Work / Milestones

### WP-00 — Baseline и контрольные сценарии
- Status: completed. Depends on: none. Acceptance: исходный уровень для AC3, AC5, AC9.
- Проверить чистоту task range, SDK, submodule и точные startup/disposal пути Static/Dynamic, OpenGL и Content host. Запустить базовые Runtime/Runner/Generator проверки из команд ниже; зафиксировать counts и доступность OpenGL probe. Не менять продуктовый код и не загрязнять рабочий каталог соседнего worktree.

### WP-01 — Формат, чтение и безопасный захват пакета
- Status: completed. Depends on: WP-00. Acceptance: AC6, AC8, AC9 (полная интеграция позже).
- Реализован в общем checkout (subagent `e0b5f0bc-ac29-4d14-aa9a-3673ef044e83`). Согласован интерфейс `ModContentPackage` (исходный manifest, store, упорядоченные `ModContentOverride`, путь); `CaptureAsync` принимает удерживаемый `previous` и возвращает независимое владение для abort/retirement. WP-02 использует эти входные данные. Общие каталоги сборки Runtime/Tests не используются конкурентно; пакет принят только после проверки реализации и тестов.
- Content Core/Runtime: прочитать descriptor v1, сторону, generation identity и bounded пакет; переиспользовать существующие проверки поколений и пути, не вводить новый build command. Захватить только manifest-listed артефакты в принадлежащую процессу staging-папку, проверить согласованность, утилизировать по владению, не трогать исходник. Предусмотреть общий вход данных с WP-02/03, без runtime startup/host регистрации в этом пакете.
- Проверки: Core 188/188, Runtime 144/144 на снимке с частью WP-02; основной агент повторил Release `--filter 'FullyQualifiedName~ModContentCaptureTests|FullyQualifiedName~ModContentPackageTests'` → 35/35. Реальный `content create`/`content build` для Client и Server без внешних целей в build: проверены байты после удаления исходников. Windows, физическая запись 4 GiB и отмена во время системного вызова не проверялись.
- Strict TDD (Red → Green → recheck): `SyntheticPackage*` — нет правил/проверки lifetime → 2/2 → 2/2; `CaptureUsesOnlySelectedSide*`/`AbsentSide*`/`RejectsWrongGeneration*` — null или нет ожидаемого отказа → 5/5 → 5/5; `InvalidOverrideRecords*`/`MalformedDeclarationContainer*` — нет правил/отказа → 7/7 → 14/14 накопительно после извлечения `ParseRule`; `ReuseRequiresMatchingSource*`/`CaptureRejectsMetadataChanged*`/`CapturedManifestEntry*` — store меняется, нет отказа, списки мутабельны → 20/20 → 20/20; `FifoIsRejectedWithoutBlockingOpen` — три таймаута с watchdog → 6/6 → 6/6 со ссылками; `CopyRejectsSizeChange*`/`GlobalQuotaAllowsExactLimit*` — размер и квота не отклоняются → 3/3 → 3/3. Остальные граничные проверки стартовали Green.

### WP-02 — Детерминированная композиция и владение snapshot
- Status: completed. Depends on: WP-00. Acceptance: AC1–AC5, AC8, AC9 (полная интеграция позже).
- Приняты `CompleteStartup` с `DeferredContentStartup`, `PrepareModUpdateAsync`, base diagnostic overload и `CommittedSnapshot.Packages` (cold borrow, владение у root). Для D5 список установленных владельцев отделён от принятых пакетов; WP-04 обнаруживает дубликаты папок, в том числе без side-пакета.
- Strict TDD (subagent `71613353-dd7e-4345-842a-908de7803464`): `DeferredStartupKeepsLegacyAvailable*` — преждевременная видимость ID → 1/1 → 17/17; `OrderedPackagesUseOriginalPayloads*`, `InvalidRulesDistinguishMissingOwners*`, `PackageConflictsAndInvalidLocalGraphs*`, `EffectiveGraphCycle*` — неверные байты и отсутствующие диагностики → 5/5 → 21/21; `HiddenBaseChange*`, `BaseReloadChangesVisibleAssets*`, `IdenticalModEntries*`, `RefreshConflicts*`, `FullyShadowedPackages*` и точный путь лога — потеря ref, нет диагностики/отказа/claim, неверный источник → 11/11 → 53/53. Промежуточная CS1729 от WP-01 не засчитана. Characterization: 128 seeded комбинаций, in-flight read + renderer claim, warmed commit/1000 reads = 0 B. Целевые тесты 56/56, полный интегрированный Release Runtime после WP-01/02 — 147/147 (основной агент).
- `ContentRegistry.cs`, `ContentSlot.cs`, связанные runtime types: композиция базовых и ordered модовых источников по D2–D8, D10, D13. Сначала валидировать исходные ID и graph, брать только исходный replacement, пропускать невалидные правила. Отделить базовый индекс по имени от эффективных ID-слотов, не менять прежние refs по формату. Подготовка старта и кандидата переиспользует один алгоритм; сохранить раннее legacy-чтение и поколения ID. Замену store определять по фактическому выбранному источнику; base-only root changes без изменения эффективных ID публиковать без ложного AssetId event. Тестировать на fixture stores, не читать Mods и не менять Runner/координатор в этом пакете.
- Runtime TDD: ID/тип/owner/порядок/отсутствие транзитивности, локальные зависимости и цикл, версия 1, legacy оригинал, same-content/reload diff и reuse, retained store/late loads/abort/shutdown. Детерминированные генеративные комбинации. Проверить warmed read path без аллокаций; границу публикации перепроверить в WP-03.

### WP-03 — Общий coordinator для двух видов обновления
- Status: completed (subagent `ad191502-088a-4ff6-9eb2-191a6734196a`). Depends on: WP-01, WP-02. Acceptance: AC3, AC5, AC7, AC8 (полная интеграция позже).
- Принят `EnqueueModRefreshAsync(capture, report)`: callback получает последний committed список пакетов и stop token, возвращает упорядоченные пакеты, installed IDs и захваты с передачей владения. Частичный захват при исключении callback освобождает сам. Базовый 2-arg enqueue и wire transport сохранены.
- Strict TDD: `BaseReloadReportsMissingTargetOrRejectsOriginalIdCollision` — нет диагностики → 2/2; `BaseAndModQueueUseActualEnqueueOrderWithoutLosingEitherLayer` — refresh завершился до очереди → 3/3; `ModCommitsAllocateZeroWhenWarmed*` — не было готовой публикации → 1/1; `FailedModPreparationOrWrapperRelease*` — не наблюдались ошибки размера/cleanup → 2/2; `StopSettlesCapture*`, `StopAcrossModPublicationBoundary*`, `AppliedRefreshAllowsOnePending*` — раннее завершение/нет публикации → 3/3. Green 20/20 с регрессиями, post-refactor Runtime 158/158; основной агент повторил `FullyQualifiedName~ContentReloadCoordinatorTests` → 20/20. Семь warmed модовых commit = 0 B; точные effective-ID notifications, no-op сохраняет ref/sequence.
- `ContentReloadCoordinator.cs` и его ближайшие Runtime contracts: принять два вида cold-запросов, сохранить supersede базового и Busy повторного модового, не вытеснять между типами. Загружать второй слой из последнего commit, удерживать пакет и входящие stores до передачи кандидату; согласовать abort/retirement/shutdown. Существующая boundary публикует готовое состояние, diff только по effective ID, без изменения протокола и без зависимости на Lua или файловый сканер.
- Runtime TDD: обе очередности обновлений, конкурентный запрос и shutdown, failed/partial/no-op, отсутствие ложных уведомлений, отказ без потери текущего store, reuse/cleanup и no allocation в warmed PublishBoundary. Базовый запрос передаёт диагностику композиции: исчезнувшая цель пропускает правило (D8), конфликт нового собственного ID базы с уже принятым модом отклоняет весь кандидат, не удаляя слой (D8/D13). Сравнить контракт транспортного applied с прежним.

### WP-04A — Единый discovery модов
- Status: completed (subagent `6b875160-9e45-4913-ab6c-c4e991d3842e`). Depends on: WP-01, WP-02. Acceptance: AC1, AC2, AC7–AC9 (полная интеграция позже).
- Приняты `ModsRegistry.StartupDiscovery` и cold `Discover(ct)`: кандидаты, установленные владельцы (в том числе дубликаты), диагностика и неизменяемый scripted-каталог. `IModsRegistry` не меняется; вызовы конструкторов source-compatible, бинарные потребители пересобираются.
- Strict TDD (`ModDiscoveryTests`): Red по порядку/пустому каталогу, флагу и конфликту entry points (7), размерам/JSON/необычным корням (3), inventory/дубликатам/повторному чтению (3), лишней DI-зависимости, проверке Lua paths у unflagged. Green и rechecks после инкрементов 67, 74, 77, 80, 81, 82/82 (включая `LuaPreparationTests`), transient WP-03 compile error не засчитан. Полный Release Runner `CI=true` в изоляции 718/718, 0 пропусков, лог `full-runner.log` проверен основным агентом.
- `ModsRegistry.cs` и метаданные: общий детерминированный discovery для cold startup и будущего refresh, независимый от Content ID. Проверить bounded `mod_info.json`, `contentOnly` и наличие Lua entry points, дубликаты, отличие нечитаемой папки от отсутствующей. Для неоднозначной установки сохранять legacy Lua-поведение; ни один дублирующий Content-пакет не принят при старте, refresh отклонён (D13). Startup публикует прежний одноразовый Lua-каталог и отдельный инвентарь всех установленных владельцев, включая content-only и моды без пакета своей стороны. Refresh читает тот же discovery без мутации Lua, не реализуя в этом пакете выбор/захват Content или Runner API. Автоматические проверки существующей сборкой Runner: startup → валидация/порядок → повторный discovery без изменения Lua-каталога.

### WP-04 — Подключить ModSet и локальный API
- Status: completed (subagent `a0e61ea2-8a0d-469a-b9a8-521adcf3c131`). Depends on: WP-01, WP-02, WP-03, WP-04A. Acceptance: AC1, AC2, AC6–AC9 (итоговая приёмка WP-05).
- Strict TDD startup: `ModStartup_OrdinalWinnerIsTheFirstIdVersionAndLegacyRemainsBase` ×4 — версия 1 читала базу вместо победителя; `ModStartup_BeforeFinalizationOnlyLegacyIsAvailableAndEndpointIsUnbound` ×2 — ID был виден слишком рано. Green 23/23 с existing base tests. Refresh: 10 фактических Reds из `ModRefresh_*` (install/update/remove ×3, rollback ×3, stop, stable Lua/Simulation, content-only→Lua и installed-only owner): `Unavailable` или запрос преждевременно завершён. Integrated Green 33/33, post-refactor targeted 122/122; full Release Runner 734/734, Generator 80/80 (источник: прогон subagent, в том числе rerun Generator после cleanup). Worker/Runner закрывают admission до teardown, общий frame boundary остаётся до bindings.
- `LuaModSetPreparation.cs` и Runner startup: использовать общий discovery из WP-04A, сохраняя одноразовый Lua каталог и его текущий порядок. Допустить только content-only изменения состава; у уже активного scripted mod разрешить смену cooked-слоя без VM. Явно завершить ID-root после ModSet до Simulation, до допуска reload. Пробросить локальный async request через `IEngineRunner.cs`/`Bootstrap.cs` и штатные регистрации; проверить Static/Dynamic, не вводить новый IPC и engine-to-Lua reference. Сохранить активацию/освобождение renderer и fallback без подключённого Modding.
- Runner/Generator TDD: настоящий пакет при запуске, раннее чтение shader fixture по имени, Static/Dynamic × Client/Server, версия 1, Lua identity и unchanged Simulation, добавление/удаление, отсутствующий активный Lua каталог, ошибки, shutdown до/после commit. Повторить существующий opt-in baseline endpoint и no-mod path. Не менять release config или unrelated modules.

### WP-05A — Нативный readback при обновлении модовой текстуры
- Status: completed (subagent `7eaba969-df46-47a7-aa50-3394ed665b3a`). Depends on: WP-01, WP-02, WP-03. Acceptance: AC7/AC8 (проверка renderer/texture lifetime, аппаратный GPU недоступен).
- Изолированная Release-сборка прошла (0 ошибок); `--content` и `--mod-refresh` повторно прошли под `SDL_VIDEODRIVER=x11 LIBGL_ALWAYS_SOFTWARE=1` с 30 s watchdog. Cooked Client → capture → coordinator: READY старая зелёная текстура пережила commit и MergeThread submit, effective ID стал красным, lazy mod ID считал красное после удаления исходника, refresh вернул зелёное. Проверены точные RGBA 0,255,0,255 → 255,0,0,255 → 255,0,0,255 → 0,255,0,255, stale refs, освобождение обеих красных текстур после drainage, очистка capture и fallback при shutdown. Программный GLX/llvmpipe; readback отдельного framebuffer, не swapchain и не доказательство исполнения на физическом GPU. Supplemental проверка после реализации, не TDD Red.
- Расширить существующий `plans/probes/content-gpu-lifetime-audit` отдельным модовым сценарием, не меняя production. Использовать реальный cooked пакет, захват и coordinator, сохранить старый frame claim на границе, проверить framebuffer old/new и удаление модового слоя, затем shutdown. Доступен нативный OpenGL/llvmpipe, аппаратная проверка остаётся непроведённой при отсутствии устройства.

### WP-05 — Сквозная приёмка, документация и независимое ревью
- Status: completed (publication blocker documented). Depends on: WP-04, WP-05A. Acceptance: AC1–AC9.
- Итоговый серийный Release-прогон `CI=true`, `-m:1 -nr:false -p:NuGetAudit=false`: Core 188/188, Runtime 158/158, Transport 38/38, Tool.Reload 26/26, Runner 734/734, Generator 80/80; всего 1224/1224, 0 пропусков. Логи в `tests/Karpik.Engine.Core.Runner.Tests/bin/asset-overrides-verification`.
- Реальный CLI: независимые patch-a/patch-b собраны для Client и Server (4 билда, 2 текстуры на пакет; без внешнего манифеста/байтов). `[]/A/B/[A,B]/[B,A]` → base/A/B/B/A в версии 1; early/legacy оригинал, invalid trailing rule не перебил победителя. Malformed batch отклонён с очисткой staging. Hidden base reload после удаления B сохранил v14, payload identity и sequence; lazy B после удаления прочитал 1 049 236 B (SHA256 `ead309f3182cf226fe6234b3ae8f37d6f63ee69e0720da3acd6d540092300814`); удаление B/A затем дало 3+3 changes и новый оригинал. Smoke log принят из `tests/obj/wp05-smoke.9ASfqWbB/smoke.log`; команды — `build-fixtures.sh` и автономный `Wp05Smoke` в том же ignored каталоге.
- Stress fixed sample: 6 publish/reuse/remove + 6 staged stop/abort; claims A/B 3/4 → 0/0, temp roots 2 → 0, квота возвращалась к нулю. Peak 2 099 136 B логических cooked bytes, наблюдаемое enqueue→ready median 9.001 ms (6.460–37.025 ms, 1 ms polling, Linux, не бенчмарк). 10 000 warmed lookup/read и 21 warmed publish/sample: 0 B caller allocations. Это поздняя приёмка, не Red.
- Через CLI собрать два независимых пакета, использовать по отдельности и вместе, проверить фактические байты/уведомления, базовый reload при подмене, явный refresh/удаление, unread-first ресурс после удаления исходника, сохранение обычного legacy пути, bound GPU lifetime при активных кадрах. Детерминированный stress с repeated refresh/abort, checksum fixture и счётчиками владения; измерить warmed reads/commit 0 B и объём/время staging на фиксированной выборке. Обновить author workflow и ADR Content snapshots; не расширять changelog/release scope без нужды.
- Независимому read-only reviewer `947fc815-54b1-4617-8654-8c87abc722a6` передан интегрированный diff после глобального прогона: AC, stale publication, identity, startup, store/GPU claims, disposal и path/size validation; основной агент примет findings и повторит затронутые проверки. Готовить именованный PR только после verification, против `codex/modding-0.7`; если публикация недоступна, дать подготовленные title/body и точный blocker.

## Review findings

- R1 [high, closed] — Принятый side-пакет удаляется из effective catalog, если папка мода и `mod_info.json` остались, но `Content/<side>` временно отсутствует в процессе копирования (D14/AC7/8). Это не uninstall. Strict TDD: воспроизвести для content-only и активного Lua; отказ при refresh должен сохранить опубликованные версии и claims.
- R2 [high, closed] — Local refresh сейчас доступен вызывающему `EngineRunner`, но документированный игровой UI не имеет ссылки на internal `Bootstrap`, а EngineRunner не экспортирует refresh game modules (D14). Нужен минимальный публичный DI-контракт из Core, тот же запрос/очередь; без UI, IPC и связи с Runner в игровом коде.
- R3 [medium, closed] — Один discovery сохраняет legacy Lua (в том числе допустимое linked script-only), но не делает ссылочные/unsupported пути cooked-кандидатами; linked cooked/flagged/dangling диагностируются. Strict discovery: 4 Linux Reds → 86/86 → 86/86; portability-policy Red → 87/87 → 87/87 (проверена ветка политики на Linux, не нативные macOS/Windows/старый Linux без statx). CS0117 от параллельной сборки не засчитана. Host seam: фактический Red при refresh из-за ложного `Expected an ordinary Content directory` → Green 1/1, добавленный linked-drift Red вернул `(1,v2)` вместо сохранённого `(2,v1)` → Green 3/3 (обычный drift и оба linked-cut cases).
- R4 [medium, closed] — Явные `dependencies: {}`/`null` или неверная более ранняя duplicate property больше не канонизируются как `[]` на package path. `ReadPackageMetadata` проверяет обе копии, базовый parser неизменен. Red «не выброшено исключение» → Green 6/6 → isolated Runtime 167/167 (включая параллельные тесты). Isolated Core 187/188: SDK fixture жёстко вычисляет repo root из стандартного bin; повторить штатным путём в итоговом прогоне.
- R5 [medium, closed] — Root/slot retirement теперь освобождает независимые claims после backend drain или последнего Release, оставляя claim небезопасного слота и sticky failure. Red `FailedSlotDrainReleasesUnrelatedRootClaimsOnCommitAbortAndShutdown` ×3 (claim 1 вместо 0) / `FinalStoreReleaseErrorsDoNotSkipLaterSlotsOrRootClaims` (2 вместо 0) → Green 4/4 → isolated targeted 71/71. Wrapper cleanup startup/refresh подтверждён Red (следующие wrappers не release) и общим Green 8/8 для R1/R2/R5. Подозрение на отдельную унаследованную последовательность lazy-load retry после failed cleanup в этот фикс не включено.
- Независимое read-only ревью `947fc815-54b1-4617-8654-8c87abc722a6` завершено: R1–R2 high, R3–R5 medium, пять подтверждённых по исходникам; reviewer не запускал тесты/сборки. Отдельные ограничения: Static host проходит ручную typed композицию, генератор тестирует фабрику отдельно; race произвольной смены `mod_info` вне конкретного D12 recheck не считается подтверждённым дефектом в этом срезе.
- Исправления начаты в strict TDD по непересекающимся областям: WP-04 владеет R1/R2/wrapper cleanup, WP-04A — R3, WP-01 — R4, WP-02 — root/slot cleanup. Одни стандартные и три выделенные папки сборки; повторная итоговая проверка и rereview по завершении.
- Host Red: R1 `ModRefresh_MissingAcceptedSideFailsWithoutUninstallingMod` ×2 (`Applied` вместо `Failed`); R2 `ModRefresh_GameSystemReceivesLocalContractInBothCompositions` ×4 (нет DI-сервиса); R5 `ModStartup_ReleasesAllWrapperClaimsAndPreservesPrimaryAndCleanupFailures` и `ModRefresh_ReleasesAllPartialWrapperClaimsAndPreservesCaptureFailure` (release counts 1/0 и 1/0/0). Все 8 → Green; post-refactor interrupted отчёт не засчитан.
- (2026-11-21) После cooldown основной агент запустил серийный post-review Release + OpenGL (`bash-60ac1a1a0536484f`, логи `tests/Karpik.Engine.Core.Runner.Tests/bin/asset-overrides-recheck`) и запросил независимый перечек R1–R5 у того же reviewer. Remote parent проверен повторно: всё ещё `4767dcad`.
- R6 [major, closed] — Legacy-only discovery проверяет только текущую Client/Server сторону, не допускает cooked capture по ссылкам. Два сквозных Red в обеих комбинациях сторон — ложный `Failed` → Green 42/42 → 42/42, no-op refactor.
- R7 [medium, closed] — Валидный linked `Mods` допускает только прежний scripted discovery, без cooked/capture и flagged mods; dangling/file root диагностируются. Два Red: пустой Lua owner в discovery и реальном host → Green 117/117 → 117/117; проверены неизменный Lua, исчезновение с restart warning и отказ при замене папки файлом. Linux links проверены; Windows не запускался.
- R8 [low, closed] — Строгий package parser отклоняет любой duplicate `dependencies`, обычный base parser сохраняет действующую семантику. В `ModCaptureRejectsMalformedDependenciesBeforeCanonicalComparison` четыре ожидаемых Red «не выброшено исключение» (две копии × невалидный ранний/два пустых массива) → Green 10/10 с исходными случаями → targeted capture/package 45/45. Отдельный isolated Core 187/187 без стандартно-расположенной fixture; isolated Runtime 172/173 содержал намеренный параллельный R9 Red, не считался регрессией R8.
- R9 [medium, closed] — Cold failure path сохраняет первую ненулевую per-slot cleanup error: обычный lazy-load retry больше не отменяет удержание небезопасного claim при shutdown. `FailedLazyLoadRetryDoesNotErasePriorUnsafeCleanupAtShutdown` Red (claim 0 вместо 1) → Green 2/2 с регрессией обычного retry → isolated targeted 72/72 после refactor. Независимые claims освобождаются; горячий путь не затронут.
- После R1–R5 основной агент получил Release Core 188/188, Runtime 168/168, Transport 38/38, Tool.Reload 26/26, Runner 749/749, Generator 80/80 (1249/1249, 0 пропусков). `--content`/`--mod-refresh` снова прошли на llvmpipe с требуемыми RGBA; `git diff --check` прошёл. Независимый перечек подтвердил исходные пять случаев и выявил R6–R9. Они назначены WP-04A (R6/R7), WP-01 (R8), WP-02 (R9), каждый Red → Green → Refactor; после — целевой перечек.
- R6/R7/R8/R9 исправлены по наблюдавшимся Reds. Финальные Release + native запущены (`bash-794d7956a18c40c8`, логи `tests/Karpik.Engine.Core.Runner.Tests/bin/asset-overrides-final`). Targeted независимое read-only rereview завершено: R6–R9 подтверждены исправленными, R1–R5 остаются закрыты, новых actionable blockers нет. Reviewer не запускал тесты и не менял файлы.
- Серийный повтор после R6–R9: Core 188/188, Runtime 173/173, Transport 38/38, Tool.Reload 26/26, Generator 80/80; обе OpenGL проверки прошли. Runner 752/753: неизменённый `LuaEcsAllocation_MixedFiveDoesNotExceedMatchedFiveFloatBaseline(count: 1)` превысил контроль на 8 B за 1000 callbacks (4120.008 против 4120 B/callback). Тот же Release binary без сборки, только `FullyQualifiedName~LuaEcsAllocation_MixedFiveDoesNotExceedMatchedFiveFloatBaseline`, прошёл 5/5: N=1 теперь ровно 4120 B/callback в обеих колонках. Единственный повтор полного Runner на тех же бинарниках, `CI=true --no-build --no-restore`, прошёл **753/753**, 0 пропусков (`asset-overrides-final/runner-no-build-confirmation.log`). Benchmark не изменяли, исходное падение сохранено в отчёте.

## Concrete Steps

Исполнять из `/home/artem/git/Karpik.Engine`; все числа записывать по фактическим выводам. Команды базовых/целевых наборов (допускается фильтр по фактическому имени новых групп с подтверждением ненулевой выборки):

```sh
# WP-00/01/02/03/05: Core and Runtime (сначала восстановить пакеты при необходимости)
dotnet test tests/Karpik.Content.Tests/Karpik.Content.Tests.csproj -c Release -m:1 -nr:false
dotnet test tests/Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -c Release -m:1 -nr:false
# WP-00/04/05: startup / integration / static composition
dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false --blame-hang --blame-hang-timeout 180s --blame-hang-dump-type none
dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -c Release -m:1 -nr:false
# WP-05: typed CLI delivery compatibility and native smoke
dotnet test tests/Karpik.Content.Tool.Reload.Tests/Karpik.Content.Tool.Reload.Tests.csproj -c Release -m:1 -nr:false
dotnet build plans/probes/content-gpu-lifetime-audit/ContentGpuLifetimeAudit.csproj -c Release -m:1 -nr:false
git diff --check
```

WP-05: запуск native probe с `--content` по инструкции `plans/probes/content-gpu-lifetime-audit/README.md`; если OpenGL недоступен, не выдавать headless за реальное подтверждение. Runner соблюдать существующую CI-сериализацию; при sandbox denial loopback использовать предусмотренное подтверждение, не обход. Команды выше не считаются выполненными, пока не появились результаты.

### Strict TDD для WP-01–WP-05

Для каждого небольшого изменения автоматизируемого поведения (включая исправления после ревью) сначала запустить новый тест на рабочем раннере и наблюдать **Red** по ожидаемому поведению, не по ошибке окружения или компиляции. Если для теста нужен новый контракт, сначала добавить только нейтральную сигнатуру/заглушку без реализации поведения. Затем сделать минимальный **Green** и повторить тест; на **Refactor** повторить целевые проверки и отметить, если чистка не понадобилась. Основной агент записывает ID теста, причину Red, результаты Green и post-refactor.

WP-00 подтверждает работоспособность тестовых раннеров. Если нужной инфраструктуры нет, сначала восстановить её и подтвердить проходящим smoke-тестом. Если автоматизированную проверку не удаётся выполнить, остановить затронутое изменение, а не подменять Red ручной проверкой. Документацию, инфраструктурные изменения и нативный приёмочный probe проверять напрямую; probe не заменяет тесты поведения или проверку реального GPU.

## Validation and Acceptance

AC1/2/4/6: независимые пакеты + валидные/невалидные цели/порядок. AC3: сравнение warmed `TryGet`/resolve/commit; ноль добавленных аллокаций и I/O/lock на кадровом пути. AC5/7: combined reload и обе очередности без lost update, реально изменённые ID only, Lua/Simulation неизменны. AC8: реальные файлы, сбои/удаление/бюджет, active/retired claims, GPU disposal. AC9: side-specific snapshot, early legacy, v1 ID и обе модели composition. Успех — ноль failures и независимое review без открытых actionable findings; пропуски и ручные проверки указывать отдельно.

## Idempotence and Recovery

Refresh no-op переиспользует снимок/ресурсы, retries разрешены после завершения/abort; failed подготовка сохраняет active. Скачки идентичности файла либо malformed пакет не приравнивать к намеренному удалению. При shutdown не освобождать stores до снятия CPU/GPU claims и завершения читающих операций. Удалять только проверенные пути в рабочей папке текущего процесса с подтверждённым последним владельцем. Ни один шаг не требует изменения parent или reset/clean. Если GitHub/remote не разрешает зависимый PR, зафиксировать blocker без ложной публикации. Проверка средствами GitHub 2026-11-20: `origin/codex/modding-0.7` на `4767dcad`, на 5 коммитов позади локального утверждённого основания `03f2ead`. Не унаследовать эти пять изменений в PR и не менять родителя без разрешения. Финальная проверка 2026-11-21 через GitHub API и `git rev-list --left-right --count` снова дала `5 0`: удалённой ветке нужен fast-forward минимум до `03f2ead` до публикации чистого зависимого PR. Ничего не отправляли.

## Prepared PR

Title: `feat: Apply and refresh mod asset overrides by AssetId`

- Problem: independently installed mods could not replace a game or mod asset through the stable Content ID or refresh the cooked layer without a restart.
- Change: compose the ordered mod layer before publishing version 1, preserve base-only legacy reads and serialize local mod refresh with base reload. Capture immutable cooked bytes outside `Mods` and retain them until all catalog, slot and read owners release. No Lua ModSet, wire protocol, CLI or manifest migration.
- Verification: Release test counts, strict Red/Green/Refactor, CLI acceptance and OpenGL software-renderer evidence are recorded above. Windows runtime and a hardware GPU were not exercised.
- Compatibility: existing source constructor calls still compile; precompiled consumers calling the changed constructors directly must rebuild. First target is `codex/modding-0.7` after the remote contains `03f2ead`, then retarget to `main` after the parent merges and verify the diff.

## Artifacts and Notes

Авторитетный [дизайн](asset-overrides-design.md); обновляемые ADR/author docs и результаты probe при выполнении. Частные типы, split файлов и тестовые fixtures допускается уточнять без изменения AC. Adjustments: r3 отделяет discovery (WP-04A) от зависимого от coordinator wiring (WP-04), чтобы реализовать независимую логику параллельно с WP-03. Утверждённые дизайн, область и AC не меняются; новой процедуры approval нет. r4 отдельно выносит нативный readback (WP-05A) из итоговой приёмки: проверка Runtime не зависит от Runner wiring; оставшиеся сквозные проверки и независимое ревью по-прежнему после интеграции.
