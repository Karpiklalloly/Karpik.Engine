# Подключать кодген модулей к зависимым сборкам

This ExecPlan is a living document maintained according to `plans/PLANS.md`.

## Текущее состояние

- Статус: запланировано.
- Сейчас: контракт согласован; подробный план подготовлен для просмотра.
- Далее: после указания на реализацию выполнить Task 1.
- Блокеры: технических нет; реализация кода пока не поручена отдельно.

## Purpose / Big Picture

Проект `<ModuleProject>.Codegen.csproj` автоматически поставляет Roslyn-генератор всем прямым и транзитивным потребителям модуля, включая внешние игры через установленный SDK. Разработчику не требуется дописывать имя генератора в общий список.

Контракт: [design](../docs/superpowers/specs/2026-10-25-module-codegen-design.md). Шаги и проверки: [подробный план](../docs/superpowers/plans/2026-10-25-module-codegen.md).

## Progress

- [x] (2026-10-25) Согласованы суффикс `.Codegen` и транзитивное распространение.
- [x] (2026-10-25) Изучены текущие точки подключения, создана ветка `codex/module-codegen`, подготовлены связанные документы.
- [ ] Task 1: обнаружение, валидация и metadata; Configurator.Tests.
- [ ] Task 2: сборка исходников, прямые/транзитивные потребители; Configurator.Tests и StaticAnalyzer.Tests.
- [ ] Task 3: SDK payload, внешняя компиляция и документация; SDK.Tasks.Tests, SDK.IntegrationTests, diff check.
- [ ] Task 4: перенос существующих Network, StatAndAbilities, Content и Core генераторов к владельцам; обновление ссылок и повторная приемка сборки/SDK.

## Surprises & Discoveries

- Генераторы сейчас перечислены в `Directory.Build.props` и `src/Karpik.Engine.Sdk/Sdk/Sdk.targets`; у каталога `src/Karpik.Engine.Tooling/EngineModuleCatalog.cs` есть сведения о модулях и зависимостях, но нет принадлежности генераторов.
- Обычные `ProjectReference` не обеспечивают требуемое транзитивное подключение Analyzer. У генератора и runtime-модуля разные роли в графе сборки.
- Configurator находится в `src/Configurator`, а не в корне; старые заметки о путях не использовать без проверки.

## Decision Log

- Решение: точное имя `<ModuleProject>.Codegen`; автор: разработчик, 2026-10-25. Причина: принадлежность без ручного списка.
- Решение: распространение по прямым и транзитивным зависимостям; подтверждено разработчиком 2026-10-25.
- Решение плана: использовать существующий граф, catalog и Analyzer items; не добавлять runtime API и новый resolver. Автор: Codex, 2026-10-25.
- Решение: перенос существующих генераторов к соответствующим владельцам входит в задачу. Указание разработчика от 2026-10-25 отменяет прежнее исключение миграции из объема работ. Core/Content.Runtime сохраняют инфраструктурную классификацию; StaticAnalyzer остается анализатором правил.

## Outcomes & Retrospective

Подготовлены только документы. Код и конфигурация сборки не изменены; dotnet-проверки не запускались. Реализация и поведенческая приемка впереди.

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

Ожидаемый итог dotnet-команд: успешные тесты, без замалчивания skipped или ограничений окружения. На стадии подготовки документов dotnet-команды не выполнялись.

## Validation and Acceptance

Прямой потребитель и потребитель через промежуточный проект компилируются с использованием сгенерированного типа. Ромб не дублирует генератор. Неактивные optional-зависимости и несвязанные проекты не получают Analyzer. Неоднозначные имена и циклы диагностируются с путями. Проверяются Client/Server/Shared, повторная сборка, совместимость старого каталога, неизвестная версия, отсутствие generator DLL и сборка вне checkout. Существующие генераторы перенесены к владельцам, старые пути и дублирующие подключения удалены, generated API сохранен. Реализация считается завершенной только после проверок Task 1–4.

## Idempotence and Recovery

Повторный Configurator дает одинаковые артефакты, повторная сборка не дублирует Analyzer. Тестовые payload создавать во временных каталогах существующего harness. Откат ограничить файлами задачи; не сбрасывать checkout целиком. Несовместимый новый формат каталога не публиковать без обновления всех его производителей/читателей и проверки диагностики.

## Artifacts and Notes

Ветка: `codex/module-codegen`, создана от `main`. Есть design, подробный план и этот обзор; продуктовой реализации пока нет. После реализации обновить ADR `docs/02_ADR/module-graph.md`, затем подготовить один PR с фактической верификацией.
