# Прямолинейный рефакторинг SDK runtime bundle

Этот ExecPlan — живой документ. Он должен поддерживаться по правилам `plans/PLANS.md`.

## Purpose / Big Picture

Сделать реализацию публикации runtime bundle понятной при последовательном чтении, не меняя её наблюдаемое поведение. После рефакторинга MSBuild-задача должна читаться как простой сценарий: проверить входные данные, собрать staging, проверить staging и опубликовать его с rollback. Детали materialization, проверки готового дерева и атомарной замены должны находиться в трёх явно названных внутренних типах.

Пользователь внешней игры не должен увидеть изменений: имена targets и task properties, layout Dynamic/Static bundle, canonical bytes marker и manifest, диагностические коды, правила reparse-point и все recovery-сценарии остаются прежними.

## Progress

- [x] (2026-09-11) Согласованы цель: прямолинейная структура без удаления проверок и recovery-веток.
- [x] (2026-09-11) Исследованы текущая задача, её unit-тесты и принятые решения о runtime bundle.
- [x] (2026-09-11) Baseline `RuntimeBundleTaskTests`: 30 passed, 3 skipped symbolic-link scenarios, 0 failed.
- [x] (2026-09-11) Проверка структуры bundle вынесена в `BuildKarpikRuntimeBundleTask.Validation.cs`; bundle suite сохранён зелёным.
- [x] (2026-09-11) Dynamic/Static materialization вынесена в `BuildKarpikRuntimeBundleTask.Materialization.cs`; bundle suite сохранён зелёным.
- [x] (2026-09-11) Публикация и rollback вынесены в `BuildKarpikRuntimeBundleTask.Publication.cs`; bundle suite сохранён зелёным.
- [x] (2026-09-11) `Karpik.Engine.Sdk.Tasks.Tests`: 75 passed, 4 skipped symbolic-link scenarios, 0 failed; Tooling: 41 passed; SDK build successful.

## Surprises & Discoveries

- Observation: `BuildKarpikRuntimeBundleTask.cs` содержит 936 строк, а `RuntimeBundleTaskTests.cs` — 867 строк.
  Evidence: подсчёт строк 2026-09-11.
- Observation: проверяемое поведение включает не только копирование файлов, но и ограниченный обход дерева, точные UTF-8/LF bytes, reparse-point защиту, Dynamic/Static layout, staging/backup/rollback.
  Evidence: `plans/versioned-sdk-external-projects-execplan.md`, решения Milestone 5; `docs/02_ADR/static-runtime-composition.md`; `Karpik.Engine.Sdk.Tasks.Tests/RuntimeBundleTaskTests.cs`.
- Observation: `AtomicDirectoryPublisher` не является заменой runtime-bundle publisher: у них разные layout и recovery-контракты.
  Evidence: `plans/content-pipeline-foundation-execplan.md`, раздел о `ContentAtomicPublisher`; `Karpik.Engine.Tooling/AtomicDirectoryPublisher.cs`.

## Decision Log

- Decision: Сохранить полную поведенческую совместимость, а не удалять редкие защитные и recovery-ветки.
  Rationale: bundle является границей запуска игры; частичное дерево, ссылка/reparse point или неверный rollback ломают уже работающий runtime.
  Date/Author: 2026-09-11 / developer and Codex
- Decision: Разделить реализацию на три внутренние конкретные класса без новых интерфейсов, фабрик или универсального pipeline framework.
  Rationale: это делает верхний сценарий линейным и не добавляет абстракций, которые пришлось бы понимать наряду с исходной логикой.
  Date/Author: 2026-09-11 / developer and Codex
- Decision: Не объединять runtime-bundle publication с `AtomicDirectoryPublisher`.
  Rationale: различаются правила ownership, layout и доказательства «полностью собранного» output; искусственное объединение увеличит риск и объём кода.
  Date/Author: 2026-09-11 / developer and Codex
- Decision: Разделить один `BuildKarpikRuntimeBundleTask` на `partial`-файлы по materialization, validation и publication вместо передачи task state между новыми объектами.
  Rationale: исходные private helper'ы используют один набор MSBuild inputs и test seam. Partial-тип сохраняет этот доступ без нового context object, интерфейсов или изменения поведения.
  Date/Author: 2026-09-11 / Codex

## Outcomes & Retrospective

`BuildKarpikRuntimeBundleTask` разделён на четыре partial-файла без изменения public task API, target inputs или файлового layout. Основной suite подтверждает сохранение Dynamic/Static publishing, validation и rollback поведения. Packager suite не завершён в текущей среде: два несвязанных теста требуют отсутствующий `Update-KarpikSdk.ps1` и доступ к Windows Registry, после чего внешний сценарий перестал завершаться; процесс проверки был остановлен.

## Context and Orientation

`Karpik.Engine.Sdk/Sdk/Sdk.targets` вызывает `BuildKarpikRuntimeBundleTask` только для Runtime-проектов сторон Client и Server. Задача в `Karpik.Engine.Sdk.Tasks/BuildKarpikRuntimeBundleTask.cs` принимает MSBuild items и строит game-owned bundle в `KarpikRuntimeBundlePath`.

Dynamic bundle содержит versioned module staging и canonical managed-assembly manifest. Static bundle не содержит managed modules и manifest, но включает Content, Mods и допустимые native assets. Оба формата должны заменять друг друга атомарно. «Proven bundle» — дерево, прошедшее строгую проверку layout, exact marker/manifest bytes, ограничений размера/глубины/числа файлов и полного отсутствия ссылок/reparse points.

Текущий `RuntimeBundleFileSystem` — намеренная test seam для инъекции ошибок `MoveDirectory` и удаления marker. Его public surface и существующие tests сохраняются. `Karpik.Engine.Sdk.Tasks.Tests/RuntimeBundleTaskTests.cs` является основным регрессионным контрактом. `Karpik.Engine.Sdk.IntegrationTests` проверяет targets и реальную внешнюю игру.

## Real-Time Assessment

Работа происходит только во время MSBuild и публикации файлов, не в `Update`, `FixedUpdate`, ECS `Run`, сетевом или render hot path. Выделения памяти, файловый ввод-вывод и обход каталогов здесь допустимы. Клиентская и серверная стороны не смешиваются: рефакторинг сохраняет текущие входы `Side` и shape-проверки. Общего конкурентного состояния не добавляется; уникальные staging-пути, ownership marker и существующий rollback остаются. Проверки строятся на существующих unit и integration тестах, а не на новых упрощённых допущениях.

## Plan of Work

Сначала выполнить baseline `RuntimeBundleTaskTests` без изменения production-кода. Затем перенести код только механически, сохранять сигнатуры внутренних методов и добавлять тесты лишь если перенос выявит отсутствующий контракт.

`BuildKarpikRuntimeBundleTask.cs` станет тонкой MSBuild-обвязкой: public constants/properties, `Execute` и единое логирование исключений. В ней останется `RuntimeBundleFileSystem`, чтобы не менять инъекцию failures в тестах. Один partial-тип будет разложен по трём файлам; это сохраняет private state без нового context object.

Разделить implementation по трём partial-файлам в `Karpik.Engine.Sdk.Tasks`:

- `BuildKarpikRuntimeBundleTask.Materialization.cs` содержит прямые ветки `MaterializeDynamic` и `MaterializeStatic`, preflight источников и копирование. Он не публикует output и не принимает решений о rollback.
- `BuildKarpikRuntimeBundleTask.Validation.cs` содержит проверку Dynamic/Static shape, markers, manifest, tree bounds, containment и reparse points. Canonical bytes и ограничения из `RuntimeBundleLayout` не копируются и не изменяются.
- `BuildKarpikRuntimeBundleTask.Publication.cs` отвечает только за owned staging, backup, marker finalization, replacement и recovery.

После каждого переноса запускать основной task suite. В конце проверить targets, pack SDK и связанные tooling/packager тесты, поскольку published bundle запускается из versioned engine installation.

## Milestones

### Milestone 1 — Baseline и сохранение публичного контракта

Запустить `RuntimeBundleTaskTests` и зафиксировать результат в Progress. Сверить, что XML в `Karpik.Engine.Sdk/Sdk/Sdk.targets`, task property names, public constants и `RuntimeBundleFileSystem` не переименованы. Никаких production-изменений в этом milestone.

### Milestone 2 — Проверка bundle

Перенести проверки completion marker, Dynamic/Static shape, canonical manifest, tree bounds и path/reparse validation в `RuntimeBundleValidator`. Сначала переносить без переписывания условий. `BuildKarpikRuntimeBundleTask` должен вызывать validator через один очевидный метод. Запустить `RuntimeBundleTaskTests`.

### Milestone 3 — Materialization

Перенести `MaterializeDynamic`, `MaterializeStatic`, preflight и file-copy helpers в `RuntimeBundleMaterializer`. Не менять порядок copy, правила collision и native filter. Запустить `RuntimeBundleTaskTests`; отдельно проверить Dynamic и Static тестовые случаи.

### Milestone 4 — Публикация и recovery

Перенести создание owned staging, backup, replacement, marker cleanup и recovery в `RuntimeBundlePublisher`. Сохранить инъекцию `RuntimeBundleFileSystem` и все exception paths. Запустить `RuntimeBundleTaskTests` и проверить failure/rollback cases.

### Milestone 5 — Integration и package

Проверить XML targets и сборку `Karpik.Engine.Sdk`. Запустить связанные Tooling и Packager suites, затем доступные SDK integration tests. Обновить этот план фактическими командами, результатами и оставшимися ограничениями.

## Concrete Steps

Рабочая директория для всех команд: `C:\Users\artem\RiderProjects\KarpikEngine`.

1. `dotnet test Karpik.Engine.Sdk.Tasks.Tests\Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false --no-restore`
   Ожидание: все доступные task tests проходят; результат записан в Progress.
2. После каждого из Milestone 2–4 повторить команду из шага 1.
3. `dotnet test Karpik.Engine.Tooling.Tests\Karpik.Engine.Tooling.Tests.csproj -m:1 -nr:false --no-restore`
4. `dotnet test Karpik.Engine.Packager.Tests\Karpik.Engine.Packager.Tests.csproj -m:1 -nr:false --no-restore`
5. `dotnet build Karpik.Engine.Sdk\Karpik.Engine.Sdk.csproj -m:1 -nr:false --no-restore`
6. `dotnet test Karpik.Engine.Sdk.IntegrationTests\Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --no-restore`

Если `--no-restore` не находит уже восстановленные assets, выполнить соответствующую команду ещё раз без `--no-restore`, зафиксировав сетевые/пакетные предупреждения отдельно от результата рефакторинга.

## Validation and Acceptance

Рефакторинг принят, когда:

- `RuntimeBundleTaskTests` проходит без изменения ожидаемых layout, marker, manifest, error и rollback assertions;
- Dynamic и Static bundle остаются взаимозаменяемыми proven outputs;
- injected failures сохраняют прошлый complete bundle или восстановленный backup и не удаляют непроверенное дерево;
- reparse point, traversal, oversized tree/file и malformed manifest по-прежнему отвергаются;
- `Sdk.targets` продолжает передавать те же параметры в `BuildKarpikRuntimeBundleTask`;
- Tooling, Packager и SDK integration suites проходят либо их инфраструктурные ограничения явно записаны;
- `Karpik.Engine.Sdk` собирается с `-m:1 -nr:false`.

## Idempotence and Recovery

Unit tests используют временные каталоги и безопасны для повторного запуска. Production-рефакторинг не должен запускать packager, внешние процессы или модифицировать engine installations. Если перенос ломает test, откатить только последний затронутый internal extraction через reviewed patch, вернуть текущую реализацию к последнему passing milestone и повторить перенос меньшими частями. Не использовать `git reset --hard` и не трогать несвязанные изменения рабочей копии.

## Artifacts and Notes

- План: `plans/sdk-runtime-bundle-straightforward-refactor-execplan.md`.
- Основной implementation: `Karpik.Engine.Sdk.Tasks/BuildKarpikRuntimeBundleTask.cs`.
- Основной contract suite: `Karpik.Engine.Sdk.Tasks.Tests/RuntimeBundleTaskTests.cs`.
- Связанные решения: `plans/versioned-sdk-external-projects-execplan.md`, `docs/02_ADR/static-runtime-composition.md`.
