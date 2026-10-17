# Group engine projects under src and tests

This ExecPlan is maintained according to `plans/PLANS.md`.

## Текущее состояние

- Статус: выполняется.
- Сейчас: перенос и проверки завершены; подготовка коммита и PR.
- Далее: публикация ветки и PR в main.
- Блокеры: нет.

## Purpose / Big Picture

Сократить число каталогов в корне: действующие проекты и инструменты разместить в `src/`, тесты и TestWorker в `tests/`. Сохранить имена проектов, namespaces и поведение. `_scripts`, `Modules`, `templates`, `Generated`, подмодули, документация и общие файлы сборки остаются в корне. Пользователь согласовал структуру и явно поручил выполнить перенос.

Подробные шаги: [implementation plan](../docs/superpowers/plans/2026-10-17-group-project-directories.md).

## Progress

- [x] (2026-10-17) Структура согласована; создана ветка `codex/group-project-directories` от чистой `main`.
- [x] (2026-10-17) M1: 38 каталогов перенесены; solution, ссылки, CI, скрипты и актуальная документация обновлены. `Configurator --generate/--validate` и `_scripts/Update-Modules.ps1` успешны; проверены буквальные пути project/import/solution. Независимое ревью не нашло регрессий.
- [x] (2026-10-17) M2: Release-сборка — 0 ошибок; финальный non-integration прогон solution — 955 passed / 9 skipped / 0 failed в 17 тестовых сборках. SDK и templates упакованы, состав пакетов проверен, imported-reference smoke успешен. Известное ограничение старого положительного solution smoke описано ниже.
- [ ] M3: изменения проверены и опубликованы отдельным PR.

## Surprises & Discoveries

- В корне есть пустые исторические каталоги без отслеживаемых файлов. Их содержимое не относится к переносу действующих проектов.
- `git status` требует `--ignore-submodules` в ограниченной среде: чтение служебного каталога вложенного подмодуля запрещено. Подмодули не перемещаем.
- `Directory.Build.props`, `Directory.Build.targets`, публикационные проекты и генерация каталога содержат явные пути.
- Подмодуль `DragonECS.Karpik.Extensions` ссылается на прежний путь ядра. Общий `Directory.Build.targets` заменяет эту ссылку на `src/Karpik.Engine.Core`; файлы подмодуля не меняются.
- 21 старый локальный каталог содержал только `bin/obj` (исходников нет). Они сохранены в игнорируемом `artifacts/legacy-root-builds`, без удаления файлов.
- `tests/StaticAnalyzer.Tests` и до переноса не входил в solution и содержал ссылку на отсутствующий `Dragon/Dragon.csproj`; восстановление этого старого тестового проекта не входит в задачу.
- Первый запуск VSTest в песочнице не смог подключиться к testhost. При запуске без ограничений тесты выполняются; код для обхода проблемы не менялся.
- Первый полный прогон нашёл две ошибочно изменённые записи `Karpik.Engine.Sdk/версия` в тестах ProjectModel. Идентификаторы восстановлены; 31/31 тест ProjectModel прошёл, финальный полный прогон повторён.
- Дополнительный старый `ExternalSolutionValidationSmoke.ps1` проходит отрицательные сценарии, но его положительный Runtime fixture не задаёт обязательный `KarpikModuleSelection`. При точном совпадении версии SDK он завершился KARPIK011. Сценарий и требование SDK существовали до переноса; изменение контракта или ремонт старого fixture исключены из этой задачи.

## Decision Log

- Decision: Один уровень `src/` и `tests/`, без тематических подкаталогов; `_scripts` сохраняется.
  Rationale: Согласовано пользователем; имена проектов уже отражают назначение.
  Date/Author: 2026-10-17 / developer and Codex.
- Decision: Вспомогательный TestWorker и тесты анализатора также находятся в `tests/`; анализатор — `src/StaticAnalyzer`.
  Rationale: Все собственные тесты и инструменты подчиняются одинаковой структуре; тесты внутри `Modules` и подмодулей остаются на месте.
  Date/Author: 2026-10-17 / Codex.

## Outcomes & Retrospective

Перенесены 22 каталога в `src/` и 16 в `tests/`. `dotnet build KarpikEngine.slnx -c Release -m:1 -nr:false -p:NuGetAudit=false` завершился успешно: 0 ошибок, 81 предупреждение, 6:33. NuGet audit отключён только аргументом локальной проверки из-за недоступности api.nuget.org в ограниченной среде; настройки CI не менялись.

Финальный `dotnet test KarpikEngine.slnx -c Release --no-build --no-restore -m:1 -nr:false --filter "Category!=Integration"` завершился с кодом 0: 955 passed, 9 skipped, 0 failed. Все 17 тестовых сборок прошли. После восстановления SDK-идентификатора в integration fixture отдельно пересобран `tests/Karpik.Engine.Sdk.IntegrationTests` до его выполнения в финальном прогоне.

`_scripts/New-KarpikSdkPackages.ps1 -SdkVersion 0.7.0-layout -Output artifacts/layout-packages` создал SDK (66 записей) и templates (142 записи); проверены tasks, SDK targets/props, content tool, core analyzer и оба template manifest. Imported-reference smoke прошёл все три проверки. Solution smoke прошёл обе отрицательные проверки; его старый положительный fixture требует отдельного ремонта профиля модулей. Семь PowerShell-скриптов прошли синтаксический разбор. Интерактивный запуск Editor и полный игровой integration smoke не выполнялись.

## Context and Orientation

`KarpikEngine.slnx` перечисляет проекты, `Directory.Build.props/targets` задают общие ссылки и проверку графа. Configurator генерирует `AutoGenerated.targets` и `Generated/*`. `_scripts` собирают SDK и дистрибутив. `.github/workflows/dotnet.yml` запускает Configurator, сборку и тесты. Тесты SDK создают внешние проекты, чья структура не должна меняться.

## Real-Time Assessment

Меняются расположение файлов и пути сборки. Алгоритмы горячих путей, размещение данных, аллокации, Client/Server/Shared, fixed dt и concurrency не меняются.

## Plan of Work

Составить однозначную таблицу старых и новых путей. Перенести собственные проекты, обновить относительные ссылки с учётом расположения обоих концов. Обновить пути в C#, скриптах, CI, solution и актуальной документации; не переписывать исторические планы. Регенерировать каталог. Проверить существующие проекты, тесты и упаковку.

## Milestones

M1 завершается проверкой существования всех project references, imports и solution paths и отсутствия старых исполняемых путей.

M2 завершается успешными Configurator generation/validation, сборкой solution, тестами и упаковкой SDK/шаблонов либо точной записью внешнего блокера.

M3 завершается проверкой diff, commit, push и созданием PR с категорией изменений, если публикация доступна.

## Concrete Steps

Все команды выполняются из корня репозитория.

1. `dotnet run --project src/Configurator/Configurator.csproj -- --generate`
2. `dotnet run --project src/Configurator/Configurator.csproj -- --validate`
3. `dotnet build KarpikEngine.slnx -c Release -m:1 -nr:false`
4. `dotnet test KarpikEngine.slnx -c Release --no-build --no-restore -m:1 -nr:false --filter "Category!=Integration"`
5. Проверить упаковку командами существующих `_scripts` и отдельными `dotnet pack`.

## Validation and Acceptance

Ссылки разрешаются, solution открывается и собирается с новыми путями. Тесты не зависят от старой глубины вложенности. SDK и шаблоны сохраняют состав и публичные имена пакетов. `_scripts` не переименован, подмодули и `Modules` не перенесены.

## Idempotence and Recovery

Переносить только заранее перечисленные каталоги внутри проверенного абсолютного корня. Повторная проверка и генерация безопасны. При откате использовать обратную таблицу путей и отдельный task commit; не удалять сторонние изменения, локальные артефакты или подмодули.

## Artifacts and Notes

Логи проверок хранятся в локальном `artifacts/layout-*.log` и не включаются в commit. Новое ADR не требуется: публичные контракты и архитектура исполнения не меняются. Возможные заметки для `docs/knowledge` (не записаны автоматически): разрешать пути относительно реального корня; адаптировать ссылки из подмодулей на уровне интеграции; проверять отдельно идентификаторы SDK и файловые пути.
