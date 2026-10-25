# Module Codegen Implementation Plan

**Goal:** подключать генератор `<ModuleProject>.Codegen` ко всем прямым и транзитивным потребителям модуля.

**Architecture:** Configurator обнаруживает принадлежность по имени проекта. Существующий граф определяет применимость, MSBuild подключает Analyzer; engine payload переносит связь во внешний SDK.

**Tech Stack:** C#, Roslyn, MSBuild, xUnit, PowerShell.

**Spec:** [согласованный контракт](../specs/2026-10-25-module-codegen-design.md).

**ExecPlan:** [статус и этапы](../../../plans/module-codegen-execplan.md).

Статус: план подготовлен; код не изменен, тесты не запускались. Реализация требует отдельного указания разработчика согласно AGENTS.md. Выполнять задачи последовательно; после каждого этапа обновлять оба плана.

## Global Constraints

- Имена проектов сопоставляются точно с суффиксом `.Codegen`; генератор необязателен.
- Подключение транзитивное, с дедупликацией на одну компиляцию.
- Существующие правила выбора модулей, optional-зависимостей и сторон сохраняются.
- Никаких новых runtime-зависимостей или изменений игрового цикла.
- Перенести существующие генераторы к владельцам по карте design; сохранить работу генераторов Core/Content.Runtime без превращения владельцев в выбираемые runtime-модули.
- Все команды выполнять из `C:\Users\artem\RiderProjects\KarpikEngine`; dotnet запускать с `-m:1 -nr:false`.

## Review Focus

- Два проекта с одинаковым именем генератора: диагностировать неоднозначность.
- Ромб зависимостей: один Analyzer, без повторной генерации.
- Optional-модуль отсутствует либо выключен: его генератор не активируется.
- Генератор зависит от собственного потребителя: диагностировать цикл до компиляции.
- Установленный SDK: генератор разрешается из payload, без путей к checkout и утечки в runtime bundle.

## Task 1: Обнаружение и build metadata

Файлы: `src/Configurator/Models.cs`, `RepositoryParser.cs`, `GraphValidator.cs`, `ArtifactGenerator.cs`; тесты `tests/Configurator.Tests/ConfiguratorTests.cs`.

Вход: проекты решения и существующий разрешенный граф модулей. Выход: необязательная однозначная связь проекта модуля с проектом генератора, доступная генерации каталога и пакетированию.

Принадлежность должна также поддерживать инфраструктурные проекты-владельцы из `src` (`Karpik.Engine.Core`, `Karpik.Content.Runtime`), сохраняя их текущую классификацию. Их применимость определяется зависимостью потребителя от владельца; выбор runtime-модулей к ним не приписывается.

- [ ] Добавить тесты `Parser_DiscoversMatchingModuleCodegen`, `Parser_AllowsModuleWithoutCodegen`, `Validator_RejectsAmbiguousModuleCodegen`, `Validator_RejectsModuleCodegenConsumerCycle`. Проверять точное имя, отсутствие ложного runtime-модуля и диагностические пути.
- [ ] Запустить `dotnet test tests/Configurator.Tests/Configurator.Tests.csproj -m:1 -nr:false`; подтвердить, что новые тесты падают из-за отсутствующей функциональности.
- [ ] Добавить минимальное поле принадлежности в существующую модель, обнаружение по имени и проверки неоднозначности/циклов. Использовать текущие правила классификации проектов и обхода графа.
- [ ] Генерировать детерминированную metadata-связь для исходников и SDK package catalog; тест должен проверять повторяемость результата и отсутствие абсолютных checkout-путей в пакетном каталоге.
- [ ] Повторить Configurator.Tests; записать результат в оба плана.

## Task 2: Подключение в сборке исходников

Файлы: `Directory.Build.targets`, `src/Configurator/ArtifactGenerator.cs`, `tests/Configurator.Tests/OptionalModuleBuildTests.cs`, `tests/StaticAnalyzer.Tests/CodegenDeploymentTests.cs`; генерируемый `Generated/KarpikModuleCatalog.props`.

Вход: связь из Task 1 и разрешенные зависимости. Выход: build-only `ProjectReference` с `OutputItemType=Analyzer` и `ReferenceOutputAssembly=false` для применимых потребителей.

- [ ] Создать временный тестовый модуль `Fixture.Core`, генератор `Fixture.Core.Codegen`, промежуточный проект и потребитель; генератор создает тип, который потребитель использует при компиляции.
- [ ] Проверить прямую зависимость, транзитивную цепочку, ромб, unrelated-проект, optional off/on, Client/Server/Shared и повторную сборку. Генератор не должен автоматически запускаться в своем проекте либо владельце.
- [ ] Запустить `dotnet test tests/Configurator.Tests/Configurator.Tests.csproj -m:1 -nr:false` и зафиксировать ожидаемое падение новых проверок.
- [ ] Подключить analyzer-ссылки через существующий build graph, дедуплицировать по нормализованному пути проекта. Не использовать обычные runtime references как транспорт генератора.
- [ ] Выполнить Configurator для обновления его артефактов принятым в репозитории способом, затем `dotnet build src/Configurator/Configurator.csproj -m:1 -nr:false` и `dotnet src/Configurator/bin/Debug/net10.0/Configurator.dll --validate`.
- [ ] Запустить Configurator.Tests и `dotnet test tests/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj -m:1 -nr:false`; записать результаты.

## Task 3: Установленный SDK и поведенческая приемка

Файлы: `src/Karpik.Engine.Tooling/EngineModuleCatalog.cs`, `src/Karpik.Engine.Sdk.Tasks/ResolveKarpikStaticReferencesTask.cs`, `src/Karpik.Engine.Sdk/Sdk/Sdk.targets`, `_scripts/New-KarpikSdkPackages.ps1` и существующий путь экспорта payload; тесты `tests/Karpik.Engine.Sdk.Tasks.Tests/ResolveKarpikStaticReferencesTaskTests.cs`, `tests/Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs`.

Вход: пакетные metadata из Task 1 и выбранный граф SDK. Выход: `Analyzer` items из engine payload для применимых компиляций в Static и Dynamic modes.

- [ ] Добавить тесты разрешения metadata, дедупликации, старого каталога без регистраций, неизвестной версии и отсутствующей заявленной DLL. Путь DLL должен оставаться внутри payload.
- [ ] Запустить `dotnet test tests/Karpik.Engine.Sdk.Tasks.Tests/Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false` и подтвердить ожидаемое падение новых тестов.
- [ ] Расширить существующий каталог и экспорт payload сведениями о генераторе; собирать и копировать generator output с необходимыми зависимостями отдельно от runtime. Проверить все текущие читатели/писатели каталога и обновить затронутые fixtures.
- [ ] Добавить выход analyzer items к существующему SDK resolution и подключить в SDK targets до CoreCompile. Не ограничивать это поведение веткой Static; при этом не подменять существующую политику runtime references.
- [ ] В ExternalGameCliTests собрать временный внешний проект с цепочкой `Game → Gameplay → Fixture.Core`, используя упакованный SDK и сгенерированный тип. Проверить обе composition modes, выключенный модуль, отсутствие генератора/его зависимостей в runtime bundle и независимость от checkout.
- [ ] Запустить SDK.Tasks.Tests и `dotnet test tests/Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false`; записать реальные результаты, включая skipped и ограничения среды.
- [ ] Обновить `docs/02_ADR/module-graph.md` и `src/Karpik.Engine.Sdk/README.md` по итоговому поведению. Выполнить `git diff --check`, проверить полный diff задачи.

## Task 4: Перенос существующих генераторов

Файлы: проекты и каталоги `src/Network.Codegen`, `src/StatAndAbilities.Codegen`, `src/Karpik.Content.Codegen`, `src/Karpik.Engine.Core.Generator`; соответствующие владельцы из design; `.slnx`, `Directory.Build.props`, SDK props/targets/package project, `_scripts/New-KarpikSdkPackages.ps1`, скрипты экспорта payload и все ссылающиеся тесты/fixtures.

Вход: работающий механизм Tasks 1–3. Выход: все существующие генераторы размещены у владельцев и подключаются новым способом, без дублирующего центрального списка.

- [ ] Перед переносом найти всех потребителей старых путей, assembly names и analyzer registrations; определить, где генератор нужен самой сборке-владельцу. Сохранить контрольные результаты существующих поведенческих тестов.
- [ ] Подтвердить карту владельцев из design по используемым контрактам. Core/Content.Runtime сохранить инфраструктурными проектами; `StaticAnalyzer` не переносить в качестве генератора.
- [ ] Переместить каждый generator project рядом с владельцем, переименовать Network и Content согласно design. Сохранить generated public API и namespaces, если переименование namespace не требуется для сборки.
- [ ] Обновить пути в решении, ProjectReference, fixture/harness, SDK package/payload и скриптах. Удалить центральные подключения только после подключения соответствующих потребителей новым механизмом. Явные build-only ссылки владельцев сохранять, если они необходимы и не создают цикл.
- [ ] Проверить отсутствие старых путей во всех активных конфигурациях, скриптах и тестах; исторические записи документации не переписывать автоматически.
- [ ] Выполнить `dotnet test tests/Network.Codegen.Tests/Network.Codegen.Tests.csproj -m:1 -nr:false`, `dotnet test tests/Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -m:1 -nr:false`, `dotnet test tests/Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false`. Для StatAndAbilities выполнить сборку реального потребителя с используемыми generated типами; зафиксировать точный выбранный проект и команду в обоих планах до выполнения.
- [ ] Повторить приемку Configurator, StaticAnalyzer и внешнего SDK из Tasks 1–3 после переноса: это новая проверка измененных путей и упаковки. Обновить документацию и выполнить `git diff --check`; записать результаты в оба плана.

## Handoff

После приемки подготовить один PR `feat: Propagate module code generators to dependent projects`. Описание: проблема центрального списка, обнаружение по имени, транзитивное подключение и SDK, перенос существующих генераторов, фактические проверки и совместимость каталога. Не публиковать незавершенную реализацию как готовую возможность.
