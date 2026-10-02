# Target-aware content build profiles

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Реализовать принятый ADR `docs/02_ADR/content-target-profiles.md` одним полным вертикальным срезом: поле `targets` в sidecar, CLI-флаг `--target Client|Server`, target-aware фильтрация и валидация в `Karpik.Content.Core`, учёт target в recipe-хеше артефактов, передача `KarpikSide` от SDK в упакованный CLI и отдельные cooked-манифесты для каждого runtime в игровом шаблоне.

Готовность выглядит так: одно дерево исходников собирает два манифеста — Client-манифест содержит Shared + Client-only ассеты, Server-манифест — Shared + Server-only ассеты. Shared-ассет даёт разные locator артефактов для каждого target, даже когда cooked bytes совпадают. `dotnet test` по content- и SDK-тестам проходит, а Client- и Server-бандлы шаблона несут каждый свой cooked manifest.

## Progress

- [ ] (2026-09-14) План создан, дизайн из 5 секций согласован в чате.
- [x] (2026-09-14) Milestone 1 done: `AssetTarget` + `AssetMeta.targets` + вывод `create`. Валидация: фильтр `AssetMetaTests|ContentMetaTemplateTests|AssetTargetTests` → 38/38; полный suite → 97/97, новых варнингов нет. TDD: RED наблюдался (компиляция без `AssetTarget`), затем GREEN.
- [x] (2026-09-14) Milestone 2 done: фильтрация/валидация по target + `KCO022`/`KCO023`. Валидация: фильтр `ContentBuildCoordinatorTests` → 19/19; полный suite → 102/102, новых варнингов нет. Отклонение: `Target` со значением по умолчанию `Shared`, а не required (см. Decision Log).
- [x] (2026-09-14) Milestone 3 done: `ContentProcessorContext` + target в recipe-хеше. Валидация: полный suite → 104/104, новых варнингов нет. TDD: поведенческий RED (`Build_SameAsset_DifferentTargets_DifferentLocators` падал на равных locator), затем GREEN.
- [x] (2026-09-14) Milestone 4 done: CLI `--target` + SDK wiring. Валидация: полный content suite → 109/109; `Sdk_packages_content_tool_and_codegen_without_checkout_references` → pass (новый ассерт `--target $(KarpikSide)`); прямое прогоняние `content.dll` по `templates/Karpik.Game/Content` для Client и Server → по 8 записей, `assetId` совпадают, locator различаются. Полный MSBuild e2e шаблона не делался: шаблон резолвит упакованный SDK `0.6.0-local`, переупаковка — чужой срез; проброс покрыт текстовым ассертом props.
- [x] (2026-09-14) Milestone 5 done: матрица валидации ADR зелёная. Финальная проверка свежими прогонами: content suite → 109/109; SDK `Sdk_packages_...` → 1/1; дифф — только файлы среза.
- [x] (2026-09-14) Переупаковка SDK: `dotnet pack Karpik.Engine.Sdk` → `0.6.0-local` в `artifacts\nuget` (gitignored), замена распаковки в global packages (бэкап: `%TEMP%\karpik-sdk-0.6.0-local-backup`). Фид в payload НЕ трогали (ломает content-hash payload, `KARPIK009` — проверено на практике, откачено). MSBuild-сборки Client и Server новым тулингом: `build succeeded: 8 assets` у обоих, `KCO301` ушёл, манифесты 8/8 с теми же `assetId` и разными locator. Фикс `KarpikContentOutput` → абсолютный путь (см. Decision Log).

## Surprises & Discoveries

- Observation: `AssetTarget.TryParse` как статический метод на enum невозможен (конфликт имён C#) — хелперы живут в `AssetTargets` (мн.ч.), желаемый API скорректирован в RED-фазе до GREEN.
  Evidence: ошибка компиляции тестов `CS0246/CS0103` до введения `AssetTargets`.
- Observation: шаблон резолвит `Karpik.Engine.Sdk 0.6.0-local` из пакета, а не из исходников репо — полный MSBuild e2e потребовал бы переупаковки SDK.
  Evidence: `templates/Karpik.Game/global.json` (`msbuild-sdks`); проверка бандлов сделана прямым прогоном свежего `content.dll` по дереву шаблона (8 записей, `assetId` совпадают, locator различаются).
- Observation: `replaceAll`-правка тестовых вызовов `Process` вставила `default` не в ту позицию — поймано компилятором (`CS1503`), исправлено точечно.
  Evidence: вывод сборки тестов после первой GREEN-пачки M3.
- Observation: копирование nupkg в фид внутри engine payload ломает его content-hash (`KARPIK009`) — payload валидируется целиком через `EngineContentHash.Compute(root)` (`Karpik.Engine.Tooling/EngineInstallationValidator.cs:240-257`). Фид откачен из бэкапа, валидность восстановлена.
  Evidence: ошибка сборки Server `KARPIK009: Payload content hash mismatch` сразу после копии; исчезла после отката.
- Observation: `KarpikContentOutput`/`KarpikContentManifest` были cwd-относительными: `GetFullPath` резолвился от каталога запуска, `Exists(manifest)` был false, `KarpikContentCopyToRuntimeOutput` молча скипался, codegen падал в `KCO301`. Предсуществующий баг, вскрыт e2e-проверкой среза.
  Evidence: `-getProperty` из корня → `...\KarpikEngine\obj\...\manifest.json`; diag-лог без упоминаний цели копирования; после фикса — проектный obj-путь, файл на месте, `KCO301` ушёл.
- Observation: найденный манифест включает codegen (`ContentRefs.g.cs`), а у шаблона нет референсов `Karpik.Content.Runtime/Core` (их нет и в payload) — Client/Server шаблона не компилируются. Разрыв codegen-среза, не этого.
  Evidence: ошибки `CS0234/CS0246` в `ContentRefs.g.cs` после фикса пути; раньше генератор молчал (meta вне конуса проекта → ноль записей).

## Decision Log

- Decision: полный срез ADR в одном ExecPlan (Core + Tool + SDK + проверка шаблона + тесты), а не сначала только Core.
  Rationale: раздел валидации ADR прямо требует SDK-passthrough и бандлы шаблона для обоих runtime; разбиение оставило бы срез непроверенным сквозным образом.
  Date/Author: 2026-09-14 / developer.
- Decision: новые коды диагностик `KCO022` (зависимость не выбрана для target) и `KCO023` (Shared зависит от одностороннего ассета) вместо переиспользования `KCO012`.
  Rationale: существующие коды идут до `KCO021`; точные сообщения сохраняют старые тесты и стабильность сортировки диагностик.
  Date/Author: 2026-09-14 / developer.
- Decision: ломающее изменение `IContentProcessor.Process` (новый параметр `ContentProcessorContext`) вместо overload с default-реализацией интерфейса.
  Rationale: в репозитории всего четыре реализации; одно явное изменение лучше вечных compat-прокладок для потребителей, которых вне репо нет.
  Date/Author: 2026-09-14 / developer.
- Decision: без изменения схемы `ContentManifest` (остаётся v1), без `content.pack`, без реструктуризации `Content/` в шаблоне, существующие `*.meta` не трогаем (отсутствие `targets` означает Shared).
  Rationale: ADR держит `assetId`/`logicalName`/исходник стабильными между target и отвергает доставку по папкам и pack-работы для этого среза; манифесты и так расходятся через locator.
  Date/Author: 2026-09-14 / developer.
- Decision: `Sdk.props` ограничивает цель `KarpikContentBuild` значениями `KarpikSide` Client/Server (как уже делает `KarpikContentCopyToRuntimeOutput`), вместо падения CLI на Shared.
  Rationale: не-runtime проекты с включённым контентом продолжают собираться (codegen остаётся на fallback `**/*.json.meta`); готовят контент только рантаймы.
  Date/Author: 2026-09-14 / developer.
- Decision: `ContentBuildOptions.Target` по умолчанию `Shared` (union = всё дерево, старое поведение), а не required-поле.
  Rationale: нулевой churn существующих вызовов — все старые тесты компилируются и ведут себя как раньше; CLI/SDK всегда передают одиночный target, так что поведение по ADR сохраняется. Отклонение от исходного «required» зафиксировано здесь.
  Date/Author: 2026-09-14 / developer.
- Decision: `KarpikContentOutput` по умолчанию — абсолютный путь от `$(MSBuildProjectDirectory)` (`Sdk.targets`), вместо cwd-относительного.
  Rationale: иначе `KarpikContentManifest` указывает мимо проектного obj, копирование в `TargetDir` скипается, а codegen вечно в `KCO301`. Строка `GetFullPath('$(KarpikContentOutput)\manifest.json')` не менялась; ассерт SDK-теста обновлён под новую первую строку.
  Date/Author: 2026-09-14 / developer.
- Decision: фикс пути оставить, красный шаблон принять (ответ разработчика на вопрос о развилке).
  Rationale: откат вернул бы молчаливую потерю cooked-манифеста; красный шаблон честно показывает разрыв codegen-среза (нет `Karpik.Content.Runtime/Core` в проекте и в payload). Follow-up — за codegen/packaging срезами.
  Date/Author: 2026-09-14 / developer.

## Outcomes & Retrospective

Срез реализован полностью по плану из 5 вех, все RED наблюдались до GREEN. Итоговые доказательства (2026-09-14): `Karpik.Content.Tests` 109/109 (было 91 до среза: +18 новых тестов), SDK-ассерт 1/1, прямые сборки дерева `templates/Karpik.Game/Content` для Client и Server дают по 8 записей с теми же `assetId` и разными locator. Матрица ADR покрыта: парсинг (M1), фильтр манифестов (M2+M4), рёбра зависимостей и `KCO022`/`KCO023` (M2), пересечение `logicalName` (M2), расхождение locator (M3), проброс `KarpikSide` (M4). Два зафиксированных отклонения: `Target` по умолчанию `Shared` вместо required; проверка шаблона прямым прогоном CLI вместо MSBuild e2e (требует переупаковки SDK — чужой срез). Runtime loader, ECS, renderer, hot path не тронуты. Ретроспектива: TDD-цикл окупился дважды (поведенческий RED locator до GREEN M3; `replaceAll`-ошибка поймана компилятором); самая дорогая находка — упакованный SDK шаблона, выясненная до, а не после попытки e2e. Переупаковка добавила три находки: payload content-hashed целиком (фид внутри него неприкасаем); cwd-относительные content-пути ломали копирование и codegen (починено абсолютным путём); найденный манифест включает codegen, которому не хватает референсов в шаблоне — шаблон Client/Server сейчас не компилируется (`CS0234/CS0246` в `ContentRefs.g.cs`), осознанно принято, follow-up за codegen-срезом.

## Context and Orientation

Нетривиальные термины. `AssetTarget` — новый flags-enum в `Karpik.Content.Core`: `Client = 1`, `Server = 2`, `Shared = Client | Server`. `targets` — необязательный канонический строковый массив в каждом sidecar `<source>.meta`, перечисляющий разрешённые target; отсутствие поля означает `Shared` для обратной совместимости. `ContentBuildProfile` — неизменяемый профиль сборки, несущий выбранный `AssetTarget` из CLI/SDK в Core. `ContentProcessorContext` — неизменяемая запись, передаваемая процессорам, чтобы будущие процессоры могли выдавать target-specific bytes без переработки API. Recipe-хеш — SHA-256-фрейминг в `ContentHashing.ComputeArtifactHash` поверх байтов исходника, канонической меты и версии процессора; этот срез добавляет в фрейминг выбранный target.

Состояние репозитория с нуля. Build-time код контента живёт в `Karpik.Content.Core` (схемы, валидация, процессоры, хеширование, манифест) и `Karpik.Content.Tool` (headless-консоль с командами `build`, `validate`, `create`, `list`, `why` в `Karpik.Content.Tool/Program.cs`). Сегодня ни один из этих файлов про target не знает: `Karpik.Content.Core/AssetMeta.cs` парсит `schemaVersion`, `assetId`, `declaredType`, `logicalName`, `importSettings`, `dependencies`; `Karpik.Content.Core/ContentBuildCoordinator.cs` требует глобальной уникальности `assetId`/`logicalName` и валидирует граф зависимостей всего дерева; `Karpik.Content.Core/ContentHashing.cs` фреймит исходник + каноническую мету + версию процессора; `Karpik.Content.Core/CanonicalJson.cs` канонизирует мету и манифест; `Karpik.Content.Core/ContentMetaTemplate.cs` штампует новые sidecar без `targets`. Обвязка SDK — в `Karpik.Engine.Sdk/Sdk/Sdk.props` (цель `KarpikContentBuild`, сейчас `build --source --output --namespace` без стороны) и `Karpik.Engine.Sdk/Sdk/Sdk.targets` (цели bundle и копирования уже ограничены `KarpikSide` Client/Server). Шаблон держит одно общее дерево в `templates/Karpik.Game/Content/`, на которое оба рантайма ссылаются через `KarpikContentSourceRoot`, равный `$(MSBuildProjectDirectory)\..\..\Content`, в `templates/Karpik.Game/Source/KarpikGame.Client/KarpikGame.Client.csproj` и `templates/Karpik.Game/Source/KarpikGame.Server/KarpikGame.Server.csproj`. Runtime-загрузчик, ECS, рендер и hot path вне скоупа — ADR запрещает их трогать.

## Real-Time Assessment

Hot path: нет. Весь меняемый код работает в build time (Editor, CLI, MSBuild). Во время сборки может аллоцировать и ходить в файлы, как и сегодня.

Бюджет аллокаций: без изменений; runtime-код не трогаем.

Раскладка данных: неприменимо; ECS-компоненты и горячие циклы не меняются.

Граница сторон: изменение усиливает разделение Client / Server / Shared, а не размывает его. Shared-проекты контент вообще не готовят (отсечены в MSBuild); Client/Server готовят только своё выбранное подмножество; Shared-ассет может зависеть только от Shared-ассетов.

Поведение тиков: не затронуто; логика геймплея и физики не меняется.

Конкуренция: новых локов, потоков и общих буферов нет. Атомарная публикация (`ContentAtomicPublisher`) переиспользуется как есть.

Валидация: юнит-тесты парсинга, тесты координатора на фильтрацию / рёбра зависимостей / пересечение `logicalName` / расхождение locator, CLI-тесты на `--target`, SDK-ассерты на проброс `--target $(KarpikSide)` и сборка Client + Server шаблона, каждая со своим cooked manifest. Бенчмарки и проверки аллокаций к build-time тулингу неприменимы.

## Plan of Work

Работа идёт пофайлово: сначала Core, затем Tool, затем SDK, затем тесты и проверка шаблона.

Сначала создать `Karpik.Content.Core/AssetTarget.cs` с flags-enum, регистронезависимым парсингом и каноническим строковым преобразованием (`Client`, `Server`). Расширить `Karpik.Content.Core/AssetMeta.cs` свойством `Targets`: парсить необязательный массив `targets` (отсутствие означает `Shared`; пустой массив, нестроковые элементы и неизвестные имена — ошибки `KCO002`), хранить флаги и учитывать канонический порядок `Client,Server` в `Equals`, `GetHashCode` и `ToCanonicalMetaJson`. Обновить `Karpik.Content.Core/CanonicalJson.cs`, чтобы `SerializeMetaCanonical` писал `targets` канонически. Обновить `Karpik.Content.Core/ContentMetaTemplate.cs`, чтобы новые sidecar содержали `"targets": ["Client","Server"]`.

Затем переработать `Karpik.Content.Core/ContentBuildCoordinator.cs`. Добавить `Target` (тип `AssetTarget`) в `ContentBuildOptions` — считать обязательным и явно обновить все существующие вызовы в тестах и Tool. Сохранить глобальную уникальность `assetId` по всему отсканированному дереву. После скана вычислить выбранное множество `(meta.Targets & requestedTarget) != 0` и замкнуть на него всё остальное: уникальность `logicalName`, поиск неизвестных/отсутствующих зависимостей, правило «Shared зависит только от Shared» и поиск циклов. Добавить коды `KCO022` и `KCO023` в класс `ContentDiagnosticCodes` в `Karpik.Content.Core/ContentDiagnostic.cs`. Процессоры гонять только по выбранным записям.

Затем добавить `Karpik.Content.Core/ContentProcessorContext.cs` как readonly record struct поверх выбранного `AssetTarget`. Поменять `IContentProcessor.Process` в `Karpik.Content.Core/IContentProcessor.cs` на приём контекста и обновить четыре реализации (`Karpik.Content.Core/RawJsonProcessor.cs`, `Karpik.Content.Core/TextureProcessor.cs`, `Karpik.Content.Core/FontJsonProcessor.cs`, `Karpik.Content.Core/ShaderProcessor.cs`) — принимают, но пока игнорируют. Расширить `Karpik.Content.Core/ContentHashing.cs`, чтобы `ComputeArtifactHash` принимал выбранный target и фреймил поле `target` с канонической строкой `Client`/`Server`; обновить единственное место вызова в координаторе. Схема манифеста остаётся v1 без новых полей.

Затем обновить `Karpik.Content.Tool/Program.cs`: `build` и `validate` требуют `--target Client|Server` (всё остальное — usage error), значение пробрасывается в `ContentBuildOptions.Target`, usage-тексты обновляются. Для `create` новых флагов не нужно сверх вывода шаблона, уже покрытого выше. В `Karpik.Engine.Sdk/Sdk/Sdk.props` дописать `--target $(KarpikSide)` в exec-команду `KarpikContentBuild` и ограничить эту цель сторонами Client/Server.

Наконец, тесты и шаблон. Расширить `Karpik.Content.Tests/AssetMetaTests.cs` (матрица парсинга), `Karpik.Content.Tests/ContentBuildCoordinatorTests.cs` (манифесты по target, матрица рёбер зависимостей, правило пересечения `logicalName`, расхождение locator при одинаковых bytes) и `Karpik.Content.Tests/CliTests.cs` (`--target` обязателен/принимается/отвергается). Добавить SDK-ассерты (в `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs` или ближайшем SDK-тесте, покрывающем `Sdk.props`), что props содержат `--target $(KarpikSide)` и side-гейт. Собрать Client и Server шаблона и убедиться, что у каждого output/bundle свой cooked `manifest.json`. Существующие `*.meta` шаблона не трогать.

## Milestones

Milestone 1 — модель данных парсится и round-trip'ится. Доказательство: новые тесты парсинга зелёные; вывод `content create` содержит оба target; старые sidecar без `targets` по-прежнему парсятся как Shared.

Milestone 2 — фильтрация и валидация учитывают target. Доказательство: одно дерево-фикстура собирает Client-манифест с Shared + Client-only записями и Server-манифест с Shared + Server-only записями; запрещённые рёбра падают с `KCO022`/`KCO023`; одинаковый `logicalName` у независимой пары Client-only/Server-only проходит, а у пересекающихся — падает с `KCO005`.

Milestone 3 — идентичность зависит от target без смены поведения. Доказательство: один и тот же Shared raw-json ассет, собранный для обоих target, даёт одинаковые cooked bytes, но разные locator артефактов; все четыре процессора компилируются под новой сигнатурой без изменения выхлопа.

Milestone 4 — CLI и SDK связаны. Доказательство: `build`/`validate` отвергают отсутствующий или неверный `--target`; SDK props передают `--target $(KarpikSide)` только для Client/Server; сквозная сборка Client + Server шаблона даёт два манифеста.

Milestone 5 — матрица валидации ADR полностью зелёная. Доказательство: каждый пункт раздела `Валидация` ADR отображён на проходящий тест или записанное наблюдение сборки в `Progress`.

Каждая веха реализации обязана заканчиваться валидацией. Предпочитать smallest relevant команду: точечные юнит-тесты, интеграционные тесты и т.д. Команду и результат записать в `Progress` до старта следующей вехи. Если валидацию прогнать нельзя — записать почему, какой риск остаётся и какой командой прогнать позже.

## Concrete Steps

Рабочий каталог всех команд — корень репозитория (`C:\Users\artem\RiderProjects\KarpikEngine`).

- Валидация Milestone 1: `dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~AssetMetaTests|FullyQualifiedName~ContentMetaTemplateTests"`. Ожидаем: все совпавшие тесты проходят.
- Валидация Milestone 2: `dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~ContentBuildCoordinatorTests"`. Ожидаем: все совпавшие тесты проходят.
- Валидация Milestone 3: тот же фильтр координатора плюс `dotnet build Karpik.Content.Core\Karpik.Content.Core.csproj -m:1 -nr:false`. Ожидаем: сборка успешна без новых варнингов, тесты зелёные.
- Валидация Milestone 4: `dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~CliTests"` и ручной пробник CLI `dotnet Karpik.Content.Tool\bin\Debug\net10.0\content.dll build --help` с видимым `--target`. Затем собрать оба runtime шаблона (точные команды сборки проектов — по обычной для репо проверке шаблона) и убедиться, что у каждого свои `Content\manifest.json` в output и bundle. Ожидаем: CLI-тесты зелёные и два разных манифеста.
- Валидация Milestone 5: `dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj -m:1 -nr:false` (весь content-suite) плюс SDK-ассерт-тесты. Ожидаем: всё зелёное; пропущенный ручной шаг записать с причиной и командой для повторного прогона.

Сборки — с `-m:1 -nr:false` по инструкции агентов репо, чтобы не плодить висящие MSBuild-ноды; голый `dotnet build` не запускать.

## Validation and Acceptance

Приёмка наблюдаема и один в один отображается на раздел `Валидация` ADR: матрица парсинга отсутствующего/Client-only/Server-only/Shared `targets`; манифесты по target из одного дерева только с выбранными ассетами; разрешённые и запрещённые рёбра зависимостей для обеих сборок; дубли `logicalName` разрешены между независимыми target и запрещены между пересекающимися; расхождение locator между target при одинаковых bytes процессоров; проброс `KarpikSide` от SDK плюс cooked-манифесты каждого runtime в бандлах шаблона. Отказы из ADR обязаны сохраняться: расположение в папках само по себе никогда не меняет состав bundle (только `targets`), а target входит в идентичность recipe до того, как любой процессор начнёт выдавать target-specific bytes.

## Idempotence and Recovery

Все шаги — правки файлов плюс прогоны тестов, их безопасно перезапускать. Сборки контента остаются атомарными: упавшая сборка не трогает ранее опубликованный output благодаря существующей связке staging + `ContentAtomicPublisher`, которую срез переиспользует без изменений. Если валидация вехи падает — чинить вперёд в перечисленных файлах; миграций и бэкфиллов данных нет (старые sidecar автоматически читаются как Shared). Откат — `git checkout` среза; частичного состояния для чистки нет, кроме stray-каталогов `.staging` в content-output, которые паблишер и так восстанавливает при следующей сборке.

## Artifacts and Notes

Главный артефакт — этот ExecPlan. Артефакты кода — новые `AssetTarget.cs` и `ContentProcessorContext.cs` плюс правки из Plan of Work. Логи тестов и выхлопы сборок шаблона кратко суммировать в `Progress` по каждой вехе. Если реализация вскроет durable архитектурное решение сверх ADR (например, семантику новых кодов диагностик, достойную сохранения), обновить `docs/02_ADR/content-target-profiles.md` или добавить follow-up ADR до закрытия; рядовых implementation-ADR не создавать.
