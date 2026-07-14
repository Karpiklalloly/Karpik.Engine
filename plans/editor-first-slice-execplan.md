# Реализовать первый срез базового редактора

Этот ExecPlan — живой документ. Его необходимо поддерживать по правилам `plans/PLANS.md`.

## Purpose / Big Picture

После выполнения плана разработчик запускает отдельное приложение `Karpik.Editor`, открывает существующий KarpikEngine-проект, запускает игровой preview в отдельном процессе и наблюдает его логи, список ECS-сущностей и компоненты выбранной сущности только для чтения. Первый срез не зависит от будущего content pipeline и не включает Asset Browser, импорт ассетов, изменение компонентов или авторинг сцены.

## Progress

- [x] (2026-07-13 23:18 +04:00) Исследованы существующие `CoreRunner`, `ProcessManager`, IPC, logger и Dragon ECS debug API.
- [x] (2026-07-13 23:18 +04:00) Создан начальный ExecPlan.
- [x] (2026-07-13 23:44 +04:00) Добавлены editor snapshot contracts, жёсткие лимиты entities/components/display values, bounded console и тесты.
- [x] (2026-07-13 23:49 +04:00) IPC расширен snapshot request/response без остановки worker; одновременные запросы сериализованы.
- [x] (2026-07-14 00:12 +04:00) Создан русскоязычный Avalonia/Dock/ReactiveUI editor shell.
- [x] (2026-07-14 00:28 +04:00) Реализованы project state, полное сохранение Dock layout, preview lifecycle, console, hierarchy и read-only inspector.
- [x] (2026-07-14 00:47 +04:00) Выполнены финальные полные тесты, build, desktop/process smoke, `git diff --check` и обновление graphify.
- [x] (2026-07-14 08:40 +04:00) Первый срез продолжен multi-session orchestration; детали находятся в `plans/editor-multisession-launch-execplan.md`.

## Surprises & Discoveries

- Observation: существующий `StateRequest` предназначен для hot reload и после сбора состояния завершает worker, поэтому его нельзя переиспользовать для live inspection.
  Evidence: `Karpik.Engine.Core.Runner/Program.cs:GetHotReloadState` устанавливает `_stateCollected = true` и `_isRunning.Value = false`.
- Observation: `ProcessManager` уже владеет cross-platform named pipe и worker lifecycle, поэтому отдельный editor transport не нужен.
  Evidence: `Karpik.Engine.Core/ProcessManagement/ProcessManager.cs` создаёт `IpcServer`, запускает worker и выполняет graceful shutdown.
- Observation: `EcsWorld.GetComponentsFor` возвращает boxed component objects и подходит только для редкого debug snapshot, не для frame loop.
  Evidence: `Dragon/EcsWorld.cs` содержит debug API и thread-static buffers; snapshot выполняется только по запросу на потоке-владельце gameplay state (simulation thread у клиента, server tick thread у сервера).
- Observation: Windows named pipes запрещены в стандартной agent sandbox, но тот же IPC test проходит вне sandbox.
  Evidence: sandbox run завершался `UnauthorizedAccessException`, approved run прошёл 1/1 за 69 ms без изменения production IPC.
- Observation: пустые Dragon ECS entity не входят в итерируемый snapshot world.
  Evidence: ограничивающий тест видел одну entity, пока каждой созданной entity не был добавлен struct-компонент.

## Decision Log

- Decision: editor запускает preview как отдельный worker process через публичный controller поверх существующего `ProcessManager`.
  Rationale: это соответствует ADR, сохраняет SDL/Veldrid lifecycle отдельно от Avalonia и повторно использует проверенный shutdown/pipe flow.
  Date/Author: 2026-07-13 / Codex
- Decision: editor snapshots определяются в `Karpik.Engine.Core`, а их создание реализуется в `Karpik.Engine.Core.Runner`.
  Rationale: editor зависит только от Core contracts; Dragon ECS, graphics и modules не протекают в UI project.
  Date/Author: 2026-07-13 / Codex
- Decision: протокол snapshot использует JSON payload и выполняется максимум несколько раз в секунду.
  Rationale: это debug/control path, а не hot path; читаемость и совместимость важнее бинарной микрооптимизации. Bounded UI buffers исключают неограниченный рост памяти.
  Date/Author: 2026-07-13 / Codex
- Decision: использовать Avalonia 12.1.0, Dock 12.0.0.2 и ReactiveUI.Avalonia 12.0.3.
  Rationale: это актуальные стабильные совместимые пакеты для `net10.0`; legacy `Avalonia.ReactiveUI` deprecated.
  Date/Author: 2026-07-13 / Codex

## Outcomes & Retrospective

Первый срез реализован как отдельный composition-root `Karpik.Editor`. UI остаётся отделённым от runtime: preview запускается существующим worker-механизмом, stdout/stderr поступают в bounded console, а ECS инспектируется immutable snapshot на main thread не чаще 4 Hz. Snapshot ограничен 2,048 entities, 4,096 components, 64 components на entity и 1,024 символами display value; concurrent IPC requests сериализованы.

Dock serializer сохраняет реальное дерево раскладки, вкладки и proportions, а workspace store сохраняет последний проект и размер окна. Короткий desktop smoke оставил процесс живым после загрузки XAML; end-to-end integration test запустил настоящий SDL2/Veldrid worker, получил snapshot и штатно остановил процесс. Asset Browser, импорт, редактирование компонентов и сцены намеренно не добавлены.

## Context and Orientation

`Karpik.Engine.Core` содержит общие application/process contracts и существующий IPC. `Karpik.Engine.Core.Runner` содержит реальный `EngineRunner`, service provider и доступ к `EcsDefaultWorld`. `ClientLauncher` является composition root и staging point для client modules. Новый `Karpik.Editor` также будет client composition root: его build должен разместить worker executable и client modules рядом с editor output.

Named pipe использует framing `[4-byte payload length][1-byte message type][payload]`. Worker читает requests в `IpcClient`, а watcher/editor — responses в `IpcServer`. Snapshot request должен быть запланирован на поток-владелец gameplay state, потому что Dragon ECS world нельзя обходить одновременно с simulation/update.

Editor UI использует MVVM. `MainWindowViewModel` владеет дочерними view-models и `EditorPreviewController`; UI получает immutable snapshots и заменяет observable collections на UI thread. Ограниченный log buffer хранит последние 2,000 строк. Snapshot polling не чаще 4 Hz и не ставит новый запрос, пока предыдущий не завершён.

## Real-Time Assessment

Hot path напрямую не изменяется: snapshot не добавляется в `Update`, `FixedUpdate`, ECS `Run` или render loop. Запрос обрабатывается на main-thread scheduler только по команде editor. Snapshot аллоцирует DTO/string/JSON, что допустимо для opt-in tooling path, но запрещено вызывать чаще установленного editor polling interval. Dragon ECS обход линейный по `EcsWorld.Entities`; компоненты читаются через существующий debug API и сразу преобразуются в строки, refs не переживают запрос.

Side boundary сохраняется: Avalonia/Dock/ReactiveUI находятся только в `Karpik.Editor`; Core DTO не зависят от UI, Dragon или Veldrid; Server/Shared modules не импортируют editor. IPC asynchronous; редкий snapshot callback сериализован с gameplay работой, а один outstanding request предотвращает накопление scheduler work.

## Plan of Work

Сначала в `Karpik.Engine.Core.Runner.Tests` добавить failing tests для snapshot capture и в новом `Karpik.Editor.Tests` — для bounded log buffer, project history и preview state transitions. Затем добавить Core DTO (`EditorRuntimeSnapshot`, `EditorEntitySnapshot`, `EditorComponentSnapshot`) и метод `IEngineRunner.CaptureEditorSnapshot`; `EngineRunner` достанет `EcsDefaultWorld` из service provider и построит read-only DTO.

После green snapshot tests расширить `IpcMessageType`, `IpcClient` и `IpcServer` request/response pair. Добавить публичный `EditorPreviewController` вокруг `ProcessManager`; controller публикует lifecycle/output events, start/stop и snapshot request, но не раскрывает pipe implementation.

Затем создать `Karpik.Editor/Karpik.Editor.csproj` и `Karpik.Editor.Tests/Karpik.Editor.Tests.csproj`, добавить их в `KarpikEngine.slnx`. Editor project будет `net10.0`, `OutputType=WinExe`, `IsCompositionRoot=true`, `IsClient=true`, с Avalonia 12.1.0, Dock 12.0.0.2 и ReactiveUI.Avalonia 12.0.3. UI создаст русскоязычные панели «Проект», «Предпросмотр», «Консоль», «Иерархия» и «Инспектор».

Наконец связать commands: открыть папку через `StorageProvider`, хранить recent projects/layout под `Environment.SpecialFolder.LocalApplicationData/KarpikEngine/Editor`, запускать worker из editor output, polling snapshots при ready preview, отображать logs/entities/components и корректно dispose subscriptions/controller при закрытии окна.

## Milestones

### Milestone 1: Contracts и snapshot capture

Файлы: `Karpik.Engine.Core/Editor/EditorRuntimeSnapshot.cs`, `Karpik.Engine.Core/IEngineRunner.cs`, `Karpik.Engine.Core/Bootstrap.cs`, `Karpik.Engine.Core.Runner/Runner.cs`, `Karpik.Engine.Core.Runner.Tests/EditorSnapshotTests.cs`.

TDD: тест создаёт `EngineRunner`, регистрирует test installer с `EcsDefaultWorld`, добавляет entity и component, вызывает capture и проверяет entity id, component type/name/value. Validation:

    dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --filter EditorSnapshotTests

Ожидается: сначала FAIL из-за отсутствующих contracts, после реализации PASS.

### Milestone 2: IPC и preview controller

Файлы: `Karpik.Engine.Core/ProcessManagement/IpcProtocol.cs`, `IpcClient.cs`, `IpcServer.cs`, `ProcessManager.cs`, новый `EditorPreviewController.cs`, `Karpik.Engine.Core.Runner/Program.cs`, tests `Karpik.Engine.Core.Runner.Tests/EditorIpcTests.cs`.

TDD: loopback pipe test отправляет request, получает snapshot response и проверяет concurrent-request guard; controller lifecycle test использует fake process boundary только там, где OS process неизбежен. Validation:

    dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --filter "EditorIpcTests|EditorSnapshotTests"

### Milestone 3: Editor model и bounded state

Файлы: `Karpik.Editor/Services/BoundedLogBuffer.cs`, `ProjectHistoryStore.cs`, `PreviewSession.cs`; tests в `Karpik.Editor.Tests`.

TDD: buffer сохраняет последние 2,000 строк и корректный порядок; project store normalizes/deduplicates paths; preview state machine rejects duplicate start and transitions through Starting/Running/Stopping/Stopped/Faulted. Validation:

    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false

### Milestone 4: Avalonia/Dock/ReactiveUI shell

Файлы: `Karpik.Editor/Karpik.Editor.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, `Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`, `ViewModels/*.cs`, `Docking/EditorDockFactory.cs`, solution entry.

Shell должен компилироваться и открывать пять панелей; commands и bindings не содержат runtime types. Validation:

    dotnet build Karpik.Editor\Karpik.Editor.csproj -m:1 -nr:false

Manual smoke: открыть editor, переместить панели, закрыть/открыть приложение и увидеть восстановленный layout.

### Milestone 5: End-to-end preview inspection

Связать controller, logs, polling и selection. Build `Karpik.Editor` as composition root, запустить sample, дождаться ready, выбрать entity и увидеть component rows. Validation:

    dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false
    dotnet build Karpik.Editor\Karpik.Editor.csproj -m:1 -nr:false

Record actual manual result in Progress. If GUI cannot be launched in agent sandbox, record the exact remaining command and risk; do not claim manual acceptance.

## Concrete Steps

Рабочая директория всех команд: `C:\Users\artem\RiderProjects\KarpikEngine`.

1. Написать и запустить failing snapshot tests.
2. Реализовать Core contracts/capture и добиться green.
3. Написать и запустить failing IPC/controller tests.
4. Реализовать protocol/controller и добиться green.
5. Создать editor test project, написать failing model tests, реализовать model services.
6. Создать Avalonia project, XAML/views/view-models/docking и собрать.
7. Связать end-to-end flow, выполнить targeted и full relevant validation.
8. Запустить `graphify update .` и обновить этот ExecPlan.

## Validation and Acceptance

Автоматическое acceptance: оба test projects проходят без failures; editor project собирается; `git diff --check` чист. Runtime snapshot test подтверждает реальное чтение Dragon ECS world. IPC test подтверждает framing и response. Buffer/state tests подтверждают bounded memory и lifecycle.

Ручное acceptance: editor открывает существующую папку; запускает preview; отображает stdout/stderr; после ready показывает сущности; selection обновляет read-only component list; Stop завершает worker; повторный Start работает; закрытие editor не оставляет worker process.

## Idempotence and Recovery

Build/test команды безопасны для повтора. Project/history/layout writes используют temp file + atomic replace, поэтому прерванная запись не портит последний валидный JSON. При неудачном worker start controller очищает pipe/process и переходит в Faulted; Stop безопасен из Stopped/Faulted. Не удалять пользовательские `.obsidian` или graphify changes. Rollback ограничивается новыми editor files и отдельно перечисленными Core IPC/capture edits; не использовать `git reset --hard`.

## Artifacts and Notes

- ADR: `docs/02_ADR/editor-desktop-stack.md`.
- Kanban: `docs/04_Roadmap/kanban-0.6-editor.md`.
- Parent milestone: `docs/04_Roadmap/kanban-0.6-2d-runtime.md`.
- Package evidence: Avalonia.Desktop 12.1.0, Dock.Avalonia 12.0.0.2, ReactiveUI.Avalonia 12.0.3 are stable `net10.0`-compatible releases as of 2026-07-13.
