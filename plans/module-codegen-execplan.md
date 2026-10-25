# Подключать кодген модулей к зависимым сборкам

This ExecPlan is a living document maintained according to `plans/PLANS.md`.

## Текущее состояние

- Статус: реализация завершена и проверена.
- Сейчас: реализация и миграция готовы к ревью в PR #105.
- Далее: ревью и слияние PR #105; issue #104 закрывается при слиянии.
- Блокеры: нет; разработчик поручил реализацию.

## Purpose / Big Picture

Проект `<ModuleProject>.Codegen.csproj` автоматически поставляет Roslyn-генератор всем прямым и транзитивным потребителям модуля, включая внешние игры через установленный SDK. Разработчику не требуется дописывать имя генератора в общий список.

Контракт: [design](../docs/superpowers/specs/2026-10-25-module-codegen-design.md). Шаги и проверки: [подробный план](../docs/superpowers/plans/2026-10-25-module-codegen.md).

## Progress

- [x] (2026-10-25) Согласованы суффикс `.Codegen` и транзитивное распространение.
- [x] (2026-10-25) Изучены текущие точки подключения, создана ветка `codex/module-codegen`, подготовлены связанные документы.
- [x] (2026-10-25) Первый red/green: ModuleCodegenTests — 3 ожидаемых падения до реализации; затем Configurator.Tests 23/23. Реальная повторная компиляция транзитивного потребителя прошла после исправления quoting MSBuild (1/1).
- [x] (2026-10-25) После переноса Configurator.Tests 24/24; SDK.Tasks 101 passed / 4 skipped; Network.Codegen 24/24; Core.Generator 53/53; Packager 30/30.
- [x] (2026-10-25) SDK task: реальная компиляция прямого и транзитивного потребителей в Static/Dynamic (8/8 с остальными первыми проверками задачи). Найденная проверка старого пути Content обновлена; повторный прогон впереди.
- [x] (2026-10-25) Финальные прогоны: Configurator 31/31, SDK.Tasks 105 passed / 4 skipped, Content 25/25, SDK.Integration 21 passed / 6 skipped. StatAndAbilities собирается без ошибок с собственным генератором.
- [x] (2026-10-25) `_scripts/New-KarpikSdkPackages.ps1 -SdkVersion 0.6.0-codegen-smoke -Output artifacts/codegen-sdk-smoke` создал оба пакета. Проверка ZIP подтвердила четыре корректные регистрации, отсутствие старых DLL и генераторов вне codegen/. Новый тест сохранения codegen при копировании prepared payload прошел 1/1 после общего прогона Packager 30/30.
- [x] (2026-10-25) `Configurator.dll --generate`, `--validate` прошли. Профили отладки перенесенных генераторов указывают на реальные проекты-потребители.
- [x] Независимое ревью и исправления выполнены; реализация подготовлена для публикации в PR #105.
- [x] Task 1: обнаружение, валидация и metadata; Configurator.Tests.
- [x] Task 2: сборка исходников, прямые/транзитивные потребители; Configurator.Tests и StaticAnalyzer.Tests (известные шесть старых ошибок fixture описаны ниже).
- [x] Task 3: SDK payload, внешняя компиляция и документация; SDK.Tasks.Tests, SDK.IntegrationTests, diff check.
- [x] Task 4: перенос существующих Network, StatAndAbilities, Content и Core генераторов к владельцам; обновление ссылок и повторная приемка сборки/SDK.
- [x] Исправления независимого ревью: optional off не создает ложный цикл; проверяются циклы через несколько генераторов; учитывается корневой redirect ProjectReference. Финальный Configurator: 31/31.
- [x] Существующие типизированные ссылки сохраняют metadata и copy-local, одновременно получая OutputItemType=Analyzer. Реальная компиляция и повторная сборка проверены; числовые имена targets допускают точки в имени потребителя.
- [x] Core.Runner 141/141, ECS.Core 34/34, Core.Generator 53/53. Четыре вложенные ECS fixture сделаны internal для доступа из генерируемого top-level registry.
- [x] После последних исправлений SDK.Tasks повторно: 105 passed / 4 skipped. Повторный запуск внутри sandbox прерывался по timeout соединения vstest/testhost; запуск вне sandbox завершился успешно.
- [x] Финальный повторный Packager вне sandbox: 31/31, включая копирование prepared codegen payload. Проверка debug targetProject от корня каждого generator project подтвердила все три пути. `git diff --check` и staged diff check прошли.

## Surprises & Discoveries

- Генераторы сейчас перечислены в `Directory.Build.props` и `src/Karpik.Engine.Sdk/Sdk/Sdk.targets`; у каталога `src/Karpik.Engine.Tooling/EngineModuleCatalog.cs` есть сведения о модулях и зависимостях, но нет принадлежности генераторов.
- Обычные `ProjectReference` не обеспечивают требуемое транзитивное подключение Analyzer. У генератора и runtime-модуля разные роли в графе сборки.
- Configurator находится в `src/Configurator`, а не в корне; старые заметки о путях не использовать без проверки.
- StaticAnalyzer.Tests: 37 passed / 6 failed. Все шесть старых fixture используют отсутствующий `EcsReadonlyPool<T>`; текущий Dragon содержит `ReadonlyEcsPool<T>`. Репродуктор `dotnet test tests/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj -m:1 -nr:false --no-restore -p:ImportDirectoryBuildTargets=false -p:BuildProjectReferences=false --filter FullyQualifiedName~ReadonlyPoolAccessWithReadSummary_IsAccepted` воспроизводит тот же CS0246 без нового подключения генераторов. Файлы этих fixture и harness не менялись; дефект не исправляется в этой задаче.

## Decision Log

- Решение: точное имя `<ModuleProject>.Codegen`; автор: разработчик, 2026-10-25. Причина: принадлежность без ручного списка.
- Решение: распространение по прямым и транзитивным зависимостям; подтверждено разработчиком 2026-10-25.
- Решение плана: использовать существующий граф, catalog и Analyzer items; не добавлять runtime API и новый resolver. Автор: Codex, 2026-10-25.
- Решение: перенос существующих генераторов к соответствующим владельцам входит в задачу. Указание разработчика от 2026-10-25 отменяет прежнее исключение миграции из объема работ. Core/Content.Runtime сохраняют инфраструктурную классификацию; StaticAnalyzer остается анализатором правил.
- Ruling: отдельный versioned `codegen/catalog.json` сохраняет старый формат `modules.catalog` и классификацию Core/Content.Runtime. SDK выбирает генераторы по фактическим ReferencePath после ResolveReferences; для Runtime учитывается выбранный граф, для Test/Tool без собственного выбора — фактические ссылки. Источник metadata тот же Configurator; нового resolver модулей нет.
- Ruling: каталог и DLL входят и в engine payload, и в SDK package. При отсутствии каталога в старом payload используется каталог SDK. Source build использует catalog-generated build-only ProjectReference, SDK — Analyzer items; служебные DLL изолированы от runtime.

## Outcomes & Retrospective

Реализованы обнаружение по имени, исключение кодгенов из runtime-модулей, транзитивные analyzer-ссылки, SDK catalog/resolution, generic упаковка и перенос четырех существующих генераторов. Генерируемые namespaces и runtime API не менялись. Прогоны и ограничения перечислены в Progress и Surprises; замечания независимого ревью исправлены и проверены регрессионными тестами.

## Context and Orientation

`src/Configurator/RepositoryParser.cs` читает проекты решения; `Models.cs` хранит проекты и зависимости; `GraphValidator.cs` проверяет граф; `ArtifactGenerator.cs` создает catalog, loader и package metadata. `Directory.Build.targets` подключает сгенерированный каталог и разворачивает ссылки модулей.

`src/Karpik.Engine.Tooling/EngineModuleCatalog.cs` представляет каталог установленного payload. `src/Karpik.Engine.Sdk.Tasks/ResolveKarpikStaticReferencesTask.cs` и SDK targets разрешают модули внешней игры. `_scripts/New-KarpikSdkPackages.ps1` участвует в подготовке SDK. Analyzer — DLL, которую Roslyn запускает при компиляции; она не должна попадать в runtime bundle.

## Real-Time Assessment

Изменяется только build-time tooling. Новых вызовов Update, FixedUpdate, ECS Run, сетевого или render loop нет. Build-time коллекции допустимы; runtime layout, fixed dt, locks и latency не меняются. Стороны проверяются прежним графом. Доказательство изоляции: проверка отсутствия generator DLL и ее служебных зависимостей в runtime bundle.

## Plan of Work

В Task 1 расширить существующую модель принадлежностью генератора и детерминированной metadata. В Task 2 применять ее при сборке исходников с дедупликацией и поведенческим тестом компиляции. В Task 3 переносить generator output в payload, разрешать Analyzer для внешней игры и описать итоговый контракт в ADR/SDK README. Подробные файлы, тесты и критерии указаны в связанном плане.

Task 4 переносит существующие генераторы по карте design: Network → Network.Shared.Core, StatAndAbilities → StatAndAbilities, Content → Karpik.Content.Runtime, Core → Karpik.Engine.Core. Обновить имена/пути, решение, ссылки потребителей, тесты, SDK packaging и скрипты; удалить старые центральные подключения без потери генерации у существующих потребителей. Поддержку инфраструктурных владельцев заложить уже в Task 1.

## Milestones

Каждый Task завершать своими проверками до перехода к следующему. Task 1 считается выполненным после тестов обнаружения/диагностик; Task 2 — после компиляции сгенерированного типа прямым и транзитивным потребителем; Task 3 — после такой же внешней компиляции через SDK в Static и Dynamic modes и проверки runtime bundle. Фактические команды и результаты записывать в оба плана.

Task 4 завершен после поведенческих тестов перенесенных генераторов, сборки потребителя StatAndAbilities и повторной приемки внешнего SDK с новыми путями. До этого перенос не считается выполненным даже при успешной сборке самих генераторов.

## Concrete Steps

Рабочий каталог: `C:\Users\artem\RiderProjects\KarpikEngine`.

1. `git status --short --branch --ignore-submodules` — убедиться в ветке задачи и сохранить чужие изменения.
2. `dotnet test tests/Configurator.Tests/Configurator.Tests.csproj -m:1 -nr:false` — обнаружение и исходники.
3. `dotnet test tests/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj -m:1 -nr:false` — сохранение существующего deployment.
4. `dotnet test tests/Karpik.Engine.Sdk.Tasks.Tests/Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false` — разрешение SDK.
5. `dotnet test tests/Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false` — поведенческая проверка внешней игры.
6. `git diff --check` — отсутствие ошибок whitespace.

Ожидаемый итог dotnet-команд: успешные тесты, без замалчивания skipped или ограничений окружения. Результаты фактически выполненных команд указаны в Progress.

## Validation and Acceptance

Прямой потребитель и потребитель через промежуточный проект компилируются с использованием сгенерированного типа. Ромб не дублирует генератор. Неактивные optional-зависимости и несвязанные проекты не получают Analyzer. Неоднозначные имена и циклы диагностируются с путями. Проверяются Client/Server/Shared, повторная сборка, совместимость старого каталога, неизвестная версия, отсутствие generator DLL и сборка вне checkout. Существующие генераторы перенесены к владельцам, старые пути и дублирующие подключения удалены, generated API сохранен. Реализация считается завершенной только после проверок Task 1–4.

## Idempotence and Recovery

Повторный Configurator дает одинаковые артефакты, повторная сборка не дублирует Analyzer. Тестовые payload создавать во временных каталогах существующего harness. Откат ограничить файлами задачи; не сбрасывать checkout целиком. Несовместимый новый формат каталога не публиковать без обновления всех его производителей/читателей и проверки диагностики.

## Artifacts and Notes

Ветка: `codex/module-codegen`, создана от `main`. Реализация, ADR и SDK README входят в PR #105; issue #104 закрывается при слиянии. Дополнительный финальный просмотр агентом после исправлений недоступен из-за лимита; выполнены самостоятельная проверка и компиляционные тесты.
