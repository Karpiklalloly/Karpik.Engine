# Unity-подобная рабочая поверхность Editor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Обновить `Karpik.Editor` до компактной Unity-подобной рабочей поверхности с перетаскиваемыми вкладками, меню/toolbar, графитовой темой, выбором плотности и раскладки `Unity`/`Debug`/`Custom`.

**Architecture:** UI остаётся отдельным Avalonia composition root. `EditorDockFactory` строит dock tree, `DockLayoutStore` сохраняет пользовательский custom tree, а существующие `ReactiveCommand` остаются единственным источником действий runtime. Настройки редактора хранятся в пользовательском workspace и не попадают в Client/Server/Shared или игровой Content.

**Tech Stack:** .NET 10, Avalonia 12.1.2, Dock.Avalonia 12.1.0.4, Dock.Model.ReactiveUI, Dock.Serializer.SystemTextJson, ReactiveUI, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-20-editor-unity-like-workspace-design.md`

## Global Constraints

- По умолчанию используется `Compact`; пользовательские режимы плотности: `Compact`, `UltraCompact`, `Large`.
- Готовые раскладки: `Unity`, `Debug`; `Custom` сохраняет текущие пользовательские dock-зоны, размеры и порядок вкладок.
- `Scene`, `Game`, `Preview` находятся в одном центральном `DocumentDock`; `Project`, `Console` — в нижней группе.
- `Undo`/`Redo` находятся в `Edit` и disabled до появления настоящих undoable-операций; пустая история не имитируется.
- Меню и toolbar используют существующие `ReactiveCommand`; глобальная command bus не добавляется.
- `Preview` не встраивает Veldrid/SDL2 в Avalonia visual tree.
- Изменения не затрагивают `Client`, `Server`, `Shared`, ECS snapshot contracts и runtime hot paths.
- Компактный стиль сохраняет кликабельную область не меньше 20 px для интерактивных элементов.

## Review Focus

- Старый workspace JSON без новых полей должен загрузиться с `Compact` и `Unity`; это проверяется в `EditorWorkspaceTests`.
- Переключение `Custom → Unity/Debug → Custom` не должно терять custom dock tree; это проверяется сериализационным тестом factory/store.
- Восстановленный dock tree с отсутствующими или неизвестными dockable IDs должен безопасно перейти к валидной preset-раскладке; это проверяется в `EditorDockFactoryTests`.
- Меню и toolbar не должны обходить `CanStartServer`, `CanAddClient`, `CanStopAll`, `CanBuild` и `CanPublish`; это проверяется тестами состояния команд и ручным запуском.
- Плотность и тема должны применяться только к editor UI и не менять project Content или runtime assemblies; это проверяется workspace/settings тестом и ручным smoke-сценарием.

## Progress

- [x] (2026-09-20 16:30 +04:00) Согласована спецификация и создан первичный ExecPlan.
- [x] (2026-09-20) Реализовать workspace settings и persistence; legacy defaults и round-trip покрыты тестами.
- [x] (2026-09-20) Реализовать dock presets и custom layout storage; Unity/Debug/Custom wiring проверен scoped review.
- [x] (2026-09-20) Реализовать Editor Settings с отдельным окном, плотностью и выбором раскладки.
- [x] (2026-09-20) Реализовать компактную тему, меню, toolbar и native hotkeys.
- [x] (2026-09-20) Закрыть review findings по сохранению Custom, legacy migration, split/floating Dock wrappers и nullable загрузке.
- [x] (2026-09-30) Полный editor suite: 141 passed, 8 явных capability/opt-in skips, 0 failed. Editor build: 0 warnings/errors. Устаревшие ожидания build configuration и регистрации console logger исправлены в fixtures.
- [x] (2026-09-30) Полный editor suite с реальным переключением двух внешних проектов: 142 passed, 7 capability skips, 0 failed (`artifacts/validation/editor-verified.trx`).
- [x] (2026-10-01) Ручная приёмка завершена: первый запуск Compact + Unity, Scene/Game/Preview и Project/Console, перестановка вкладок, восстановление порядка/размеров, Debug/Custom и плотность; server + два clients, Stop all, Build, Debug/Release Publish. Server Hierarchy/Inspector показывают Entity 1 и GameComponent.Value=42; пустой client очищает обе панели, возврат к server восстанавливает сущность. Source Content не изменён, исходные настройки пользователя восстановлены и проверены по hash.

## Surprises & Discoveries

- Observation: `EditorDockFactory` уже использует `ToolDock` слева и снизу, `DocumentDock` в центре, а `DockLayoutStore` сериализует весь root tree.
  Evidence: `Karpik.Editor/Docking/EditorDockFactory.cs` и `Karpik.Editor/Docking/DockLayoutStore.cs`.
- Observation: текущий центр содержит только один `preview` document, поэтому Scene/Game требуют изменения dock tree, а не отдельного окна.
  Evidence: `EditorDockFactory.CreateLayout` создаёт один `Document` с ID `preview`.
- Observation: runtime commands уже представлены `ReactiveCommand` в `EditorShellViewModel`; отдельная command bus не нужна.
  Evidence: `StartServerCommand`, `AddClientCommand`, `StopAllCommand`, `BuildProjectCommand`, `PublishProjectCommand` и `CheckRuntimeCommand` в `Karpik.Editor/ViewModels/EditorShellViewModel.cs`.
- Observation: измененный editor компилируется отдельно без ошибок и warnings (`dotnet build ... --no-restore -p:BuildProjectReferences=false`), но полный build останавливается на отсутствующих `first-parties/Karpik.Jobs` и `DragonECS.Karpik.Extensions`.
  Evidence: финальная isolated editor compile завершилась с 0 предупреждений и 0 ошибок; full single-node build завершается 19 ошибками в `Karpik.Engine.Core` из-за отсутствующих first-party projects.
- Observation: targeted tests компилируются до шага копирования, но запуск блокируется отсутствующими `Karpik.Engine.Core.Runner` runtimeconfig/deps/apphost artifacts; ручной UI smoke в этой среде не выполнялся.
  Evidence: `dotnet test ... --no-restore -p:BuildProjectReferences=false --filter ...` завершился MSB3030.

## Decision Log

- Decision: использовать `Dock.Avalonia` для reorder/dock/floating вкладок и сохранить существующий serializer.
  Rationale: эта зависимость уже используется проектом и соответствует принятому desktop-stack ADR.
  Date/Author: 2026-09-20 / Codex.
- Decision: хранить custom layout отдельно от сгенерированных Unity/Debug preset trees.
  Rationale: выбор готовой схемы не должен уничтожать размеры, порядок и dock-зоны пользователя.
  Date/Author: 2026-09-20 / Codex.
- Decision: не добавлять undo stack, scene authoring или embedded Veldrid viewport в этот срез.
  Rationale: это отдельные подсистемы с собственными контрактами; текущий срез меняет оболочку и команды навигации.
  Date/Author: 2026-09-20 / Codex.
- Decision: считать isolated editor compile достаточной статической проверкой измененного UI при неполном checkout, но не подменять им полный acceptance.
  Rationale: missing first-party/runtime build artifacts находятся вне diff; ручной smoke и полный test pass должны быть повторены после восстановления зависимостей.
  Date/Author: 2026-09-20 / Codex.

## Outcomes & Retrospective

Implementation outcome: workspace settings, Unity/Debug/Custom dock presets, custom-layout migration, Editor Settings, compact graphite shell, toolbar commands and native hotkeys реализованы. Scoped reviews approved all implementation slices; follow-up fixes закрыли Custom seeding/preservation, split/floating/pinned Dock wrappers, legacy Scene/Game titles, failed-Custom recovery, density-only apply и nullable loading. Custom layout is persisted separately and restored through the MainWindow selection path.

Validation outcome (2026-10-01): the full editor build succeeds, and the complete test suite passes 142 tests with seven explicit capability skips, including real server/two-client teardown and external-project replacement. Desktop checks confirm initial Compact/Unity layout, tab order and splitter persistence, Debug/Custom restoration, density persistence, UI server/two-client start and Stop all, Build, Debug/Release Publish, and session-selected Hierarchy/Inspector. Selecting the server exposes Entity 1 and GameComponent.Value=42; the empty client clears both panels and returning to the server restores the entity/component. Source Content is unchanged and original settings are restored. Task 5 acceptance is complete; local evidence is `artifacts/validation/desktop-tail-20261001.md`.

## Context and Orientation

`Karpik.Editor` — отдельное Avalonia-приложение. `MainWindow.axaml` сейчас содержит меню, крупную action bar и `DockControl`. `EditorDockFactory` строит dock tree из левой группы project/hierarchy/sessions, центрального preview document, правого inspector и нижней console. `EditorWorkspace` сохраняет solution path, размеры окна и панели; `WorkspaceStore` пишет JSON в LocalApplicationData. `EditorShellViewModel` owns project/session lifecycle and exposes the existing ReactiveUI commands. `DockContextDataTemplate` presents dock contexts without exposing ECS pools or graphics objects to Avalonia.

The implementation must preserve this lifecycle boundary: layout/settings code may recompose UI objects, but it must not start, stop, or mutate runtime sessions except through the existing shell commands.

## Real-Time Assessment

The work is editor-only and does not run from `Update`, `FixedUpdate`, ECS `Run`, serialization loops, network pumps, or render loops. UI layout, JSON persistence, and Avalonia bindings may allocate on the desktop UI thread; no allocation budget is added to runtime hot paths. Dock trees are small UI graphs and are not gameplay data. Client/Server/Shared project boundaries remain unchanged. No fixed-dt, network delivery, ECS layout, lock, job, or shared-buffer behavior changes. Validation is the targeted editor test project, single-node editor build, and the manual lifecycle scenario in Task 5.

## Plan of Work

First persist the two new workspace choices with legacy defaults. Then add generated Unity/Debug dock trees and an isolated custom layout store. Next add the settings window that applies both choices atomically. Finally replace the shell presentation with compact graphite styles, the agreed menu/toolbar, and native key bindings, then run the complete editor regression and manual acceptance flow.

### Task 1: Workspace settings model and persistence

**Files:**
- Modify: `Karpik.Editor/Models/EditorWorkspace.cs`
- Test: `Karpik.Editor.Tests/EditorWorkspaceTests.cs`

**Interfaces:**
- Produces `EditorUiDensity { Compact, UltraCompact, Large }`.
- Produces `EditorLayoutPreset { Unity, Debug, Custom }`.
- Extends `EditorWorkspace` with `UiDensity` defaulting to `Compact` and `LayoutPreset` defaulting to `Unity`.
- Keeps `WorkspaceStore.LoadAsync` and `SaveAsync` signatures unchanged.

- [ ] **Step 1: Add failing persistence tests**

Add tests beside `WorkspaceStore_RestoresSolutionAndLayout`:

```csharp
[Fact]
public async Task WorkspaceStore_RoundTripsDensityAndLayoutPreset()
{
    string path = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}", "workspace.json");
    var store = new WorkspaceStore(path);

    await store.SaveAsync(new EditorWorkspace
    {
        UiDensity = EditorUiDensity.UltraCompact,
        LayoutPreset = EditorLayoutPreset.Custom
    }, TestContext.Current.CancellationToken);

    EditorWorkspace restored = await store.LoadAsync(TestContext.Current.CancellationToken);

    Assert.Equal(EditorUiDensity.UltraCompact, restored.UiDensity);
    Assert.Equal(EditorLayoutPreset.Custom, restored.LayoutPreset);
}

[Fact]
public async Task WorkspaceStore_LegacyWorkspaceUsesNewDefaults()
{
    string directory = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
    string path = Path.Combine(directory, "workspace.json");
    Directory.CreateDirectory(directory);
    await File.WriteAllTextAsync(path, "{\"SolutionPath\":\"C:\\\\games\\\\sample\\\\Sample.slnx\"}");

    try
    {
        EditorWorkspace restored = await new WorkspaceStore(path).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EditorUiDensity.Compact, restored.UiDensity);
        Assert.Equal(EditorLayoutPreset.Unity, restored.LayoutPreset);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}
```

- [ ] **Step 2: Run the focused tests and verify the new members are missing**

Run from `C:\Users\artem\RiderProjects\KarpikEngine`:

```powershell
dotnet test Karpik.Editor.Tests/Karpik.Editor.Tests.csproj -m:1 -nr:false --filter FullyQualifiedName~EditorWorkspaceTests
```

Expected: compile failure because `EditorUiDensity`, `EditorLayoutPreset`, and the new workspace properties do not exist.

- [ ] **Step 3: Implement the minimal model change**

Add the two enums and init-only workspace properties in `Karpik.Editor/Models/EditorWorkspace.cs` without changing workspace file location or async store APIs:

```csharp
public enum EditorUiDensity { Compact, UltraCompact, Large }
public enum EditorLayoutPreset { Unity, Debug, Custom }

public sealed class EditorWorkspace
{
    public string? SolutionPath { get; init; }
    public EditorUiDensity UiDensity { get; init; } = EditorUiDensity.Compact;
    public EditorLayoutPreset LayoutPreset { get; init; } = EditorLayoutPreset.Unity;
    // existing size properties remain unchanged
}
```

- [ ] **Step 4: Run the focused tests and verify they pass**

Run the same command from the repository root. Expected: all `EditorWorkspaceTests` pass, including legacy defaults.

- [ ] **Step 5: Commit the model and tests**

```powershell
git add Karpik.Editor/Models/EditorWorkspace.cs Karpik.Editor.Tests/EditorWorkspaceTests.cs
git commit -m "feat(editor): persist density and layout preset"
```

### Task 2: Dock tree presets and custom layout storage

**Files:**
- Modify: `Karpik.Editor/Docking/EditorDockFactory.cs`
- Modify: `Karpik.Editor/Docking/DockLayoutStore.cs`
- Test: `Karpik.Editor.Tests/EditorDockFactoryTests.cs`
- Test: `Karpik.Editor.Tests/EditorWorkspaceTests.cs`

**Interfaces:**
- `EditorDockFactory.CreateLayout(EditorLayoutPreset preset = EditorLayoutPreset.Unity)` returns a generated valid `IRootDock` for `Unity` or `Debug`; `Custom` is loaded by `MainWindow` through `DockLayoutStore` and falls back to the Unity tree on load failure.
- Existing `CreateLayout()` callers continue to compile through the default argument.
- `DockLayoutStore` gains `CreateCustom()` and `CreateCurrent()` factories using separate user files; both reuse `Save(IRootDock)` and `Load()`.
- Preset generation never mutates the loaded custom tree.

- [ ] **Step 1: Add failing dock-tree tests**

Add tests asserting the required IDs and groups:

```csharp
[Fact]
public void CreateUnityLayout_PlacesDocumentsInOneCenterDock()
{
    using var shell = CreateShell();
    IRootDock root = new EditorDockFactory(shell, new EditorWorkspace()).CreateLayout(EditorLayoutPreset.Unity);

    IDockable documents = FindById(root, "documents")!;
    var documentDock = Assert.IsType<DocumentDock>(documents);
    Assert.Equal(["scene", "game", "preview"], documentDock.VisibleDockables!.Select(x => x.Id));
}

[Fact]
public void CreateDebugLayout_UsesTheSameDocumentsAndExpandedBottomDock()
{
    using var shell = CreateShell();
    IRootDock root = new EditorDockFactory(shell, new EditorWorkspace()).CreateLayout(EditorLayoutPreset.Debug);

    Assert.Equal(["scene", "game", "preview"], FindById(root, "documents")!.VisibleDockables!.Select(x => x.Id));
    Assert.True(Assert.IsType<ToolDock>(FindById(root, "bottom-tools")).Proportion > 0.28);
}
```

Add a round-trip test that saves a custom dock tree, creates a preset, then reloads the custom tree and asserts its tab order and proportions are unchanged.

- [ ] **Step 2: Run the focused tests and verify the current single-document layout fails**

Run:

```powershell
dotnet test Karpik.Editor.Tests/Karpik.Editor.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~EditorDockFactoryTests|FullyQualifiedName~EditorWorkspaceTests"
```

Expected: failure because the current factory creates only `preview` in `documents` and has no preset/custom storage.

- [ ] **Step 3: Implement the smallest preset tree**

Refactor `EditorDockFactory.CreateLayout` to create `Tool` IDs `hierarchy`, `sessions`, `project`, `inspector`, `console` and `Document` IDs `scene`, `game`, `preview`. Put `scene`, `game`, and `preview` in the same `DocumentDock`; keep `CanClose = false` for the three base documents. Keep the existing `PreviewViewModel` context for `preview` and use small read-only editor surface contexts for `scene` and `game` until real authoring/embedded rendering exists.

Set the `Unity` tree to the agreed proportions and set the `Debug` tree to the same structure with `bottom-tools` proportion `0.36`. `Custom` uses the loaded custom tree; if it is missing or cannot be deserialized, return the Unity tree.

- [ ] **Step 4: Add current/custom layout stores without changing serializer format**

Keep `DockLayoutStore.Save(IRootDock)` atomic. Add path factories under the same `LocalApplicationData/KarpikEngine/Editor` directory:

```csharp
public static DockLayoutStore CreateCurrent() =>
    new(Path.Combine(GetEditorDirectory(), "layout-current-v2.json"));

public static DockLayoutStore CreateCustom() =>
    new(Path.Combine(GetEditorDirectory(), "layout-custom-v2.json"));

private static string GetEditorDirectory() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KarpikEngine", "Editor");
```

When migrating from the existing `layout-v2.json`, treat it as the first custom/current layout before writing the new files. Do not delete the old file automatically.

- [ ] **Step 5: Run the dock tests and verify preset/custom round trips**

Run the focused command again. Expected: the factory tests pass, including tab order, `Debug` proportions, and preservation of custom layout data.

- [ ] **Step 6: Commit the dock work**

```powershell
git add Karpik.Editor/Docking/EditorDockFactory.cs Karpik.Editor/Docking/DockLayoutStore.cs Karpik.Editor.Tests/EditorDockFactoryTests.cs Karpik.Editor.Tests/EditorWorkspaceTests.cs
git commit -m "feat(editor): add dock layout presets"
```

### Task 3: Editor Settings window and applying density/layout

**Files:**
- Create: `Karpik.Editor/Settings/EditorSettingsWindow.axaml`
- Create: `Karpik.Editor/Settings/EditorSettingsWindow.axaml.cs`
- Create: `Karpik.Editor/Settings/EditorSettingsViewModel.cs`
- Modify: `Karpik.Editor/MainWindow.axaml.cs`
- Modify: `Karpik.Editor/Models/EditorWorkspace.cs`
- Test: `Karpik.Editor.Tests/EditorWorkspaceTests.cs`

**Interfaces:**
- `EditorSettingsViewModel` exposes `EditorUiDensity UiDensity`, `EditorLayoutPreset LayoutPreset`, `ApplyCommand`, and `CancelCommand`.
- `EditorSettingsWindow` accepts current settings and an `Action<EditorUiDensity, EditorLayoutPreset>` apply callback; it does not access runtime sessions or ECS state.
- `MainWindow.ApplyEditorSettings(EditorUiDensity density, EditorLayoutPreset preset)` applies styles, saves workspace settings, and replaces only the dock layout.

- [ ] **Step 1: Add a settings view-model test**

Test that applying selected values invokes the callback once with the selected density and layout, while cancel invokes no callback.

- [ ] **Step 2: Run the focused test and verify it fails**

```powershell
dotnet test Karpik.Editor.Tests/Karpik.Editor.Tests.csproj -m:1 -nr:false --filter FullyQualifiedName~EditorSettings
```

Expected: compile failure because the settings view-model and window do not exist.

- [ ] **Step 3: Implement the settings window**

Use two compact `ComboBox` controls (`Compact/UltraCompact/Large` and `Unity/Debug/Custom`) plus `Apply` and `Cancel`. Keep the window modeless or single-instance so repeated `Window → Editor Settings…` opens/focuses the same window instead of creating duplicates.

- [ ] **Step 4: Apply settings through MainWindow**

On Apply:

1. save the active dock tree to the custom store;
2. update the workspace `UiDensity` and `LayoutPreset` values;
3. apply the density resource class;
4. load the selected custom tree or generate the selected preset;
5. attach contexts and assign `DockHost.Layout`;
6. save current layout and workspace.

If a custom layout fails to load, report the error through `EditorShellViewModel.ReportEditorMessage` and use the Unity preset without terminating the editor.

- [ ] **Step 5: Run settings and workspace tests**

Run the settings filter and the complete `EditorWorkspaceTests` filter. Expected: settings apply/cancel behavior and persistence pass.

- [ ] **Step 6: Commit the settings flow**

```powershell
git add Karpik.Editor/Settings Karpik.Editor/MainWindow.axaml.cs Karpik.Editor/Models/EditorWorkspace.cs Karpik.Editor.Tests/EditorWorkspaceTests.cs
git commit -m "feat(editor): add editor settings window"
```

### Task 4: Compact theme, menu, toolbar, and native hotkeys

**Files:**
- Modify: `Karpik.Editor/App.axaml`
- Create: `Karpik.Editor/Styles/EditorTheme.axaml`
- Modify: `Karpik.Editor/MainWindow.axaml`
- Modify: `Karpik.Editor/MainWindow.axaml.cs`
- Modify: `Karpik.Editor/ViewModels/EditorShellViewModel.cs` only if a missing command state is required
- Test: `Karpik.Editor.Tests/EditorCommandPresentationTests.cs`

**Interfaces:**
- Existing shell commands remain the source for `▶S`, `▶C`, Stop, Build, Publish, and runtime check.
- UI-only file/workspace actions may be window-level commands because they require `StorageProvider` and window dimensions.
- `KeyBinding` routes to the same command instance as its menu item; no manual global `KeyDown` switch is introduced.

- [ ] **Step 1: Add a command presentation test for state forwarding**

Pin that the toolbar/menu use the same command instances by exposing the command bindings through the shell and asserting `CanStartServer`, `CanAddClient`, `CanStopAll`, `CanBuild`, and `CanPublish` are reflected after state changes.

- [ ] **Step 2: Implement the graphite theme resources**

Create `EditorTheme.axaml` with resources for `#1E1E1E`, `#252526`, `#2D2D30`, `#3E6EAA`, compact tab/button/list styles, and density-specific resource classes. Keep the minimum interactive control height at 20 px.

- [ ] **Step 3: Replace the current two-menu shell with the agreed menu**

In `MainWindow.axaml`, replace the current `Файл`/`Запуск` menu and large action bar with `File`, `Edit`, `Assets`, `GameObject`, `Window`, and `Help`, followed by the compact toolbar:

```xml
<Button Content="▶S" Command="{Binding StartServerCommand}" IsEnabled="{Binding CanStartServer}" />
<Button Content="▶C" Command="{Binding AddClientCommand}" IsEnabled="{Binding CanAddClient}" />
<Button Content="■" Command="{Binding StopAllCommand}" IsEnabled="{Binding CanStopAll}" />
<Button Content="Build" Command="{Binding BuildProjectCommand}" IsEnabled="{Binding CanBuild}" />
```

Keep `Undo`/`Redo` in `Edit` only and disabled until an undo stack exists. Keep `Editor Settings…` under `Window`.

- [ ] **Step 4: Add native key bindings**

Bind `Ctrl+O`, `Ctrl+S`, `Ctrl+Shift+S`, `Ctrl+Z`, `Ctrl+Shift+Z`, `F`, `Delete`, and `Alt+F4` using Avalonia `KeyBinding`/`HotKey`. Route file-dialog actions through the existing `OpenProject_OnClick` behavior and route runtime actions through the shell commands.

- [ ] **Step 5: Run build and targeted UI tests**

Run:

```powershell
dotnet build Karpik.Editor/Karpik.Editor.csproj -m:1 -nr:false
dotnet test Karpik.Editor.Tests/Karpik.Editor.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~EditorCommandPresentationTests|FullyQualifiedName~DockContextDataTemplateTests"
```

Expected: build succeeds, command presentation tests pass, and no Client/Server/Shared project references change.

- [ ] **Step 6: Commit the shell presentation**

```powershell
git add Karpik.Editor/App.axaml Karpik.Editor/Styles/EditorTheme.axaml Karpik.Editor/MainWindow.axaml Karpik.Editor/MainWindow.axaml.cs Karpik.Editor/ViewModels/EditorShellViewModel.cs Karpik.Editor.Tests
git commit -m "feat(editor): add compact Unity-like shell"
```

### Task 5: Full regression and manual acceptance

**Files:**
- Modify: `docs/04_Roadmap/kanban-0.6-editor.md` only for completed scope/checkmarks after validation.
- Modify: `docs/superpowers/plans/2026-09-20-editor-unity-like-workspace.md` to record progress and results.

- [x] **Step 1: Run the complete editor test project**

```powershell
dotnet test Karpik.Editor.Tests/Karpik.Editor.Tests.csproj -m:1 -nr:false
```

Expected: all existing editor tests and new layout/settings tests pass.

- [x] **Step 2: Run the editor build**

```powershell
dotnet build Karpik.Editor/Karpik.Editor.csproj -m:1 -nr:false
```

Expected: zero errors and no new runtime project references.

- [x] **Step 3: Execute the manual workspace scenario**

From the repository root, launch the editor and verify:

1. first launch is Compact + Unity;
2. central tabs are Scene/Game/Preview and bottom tabs are Project/Console;
3. drag/reorder tabs, resize dock zones, close/reopen the editor, and confirm the layout returns;
4. switch to Debug, confirm Console expands, then switch to Custom and confirm the previous custom arrangement returns;
5. change density in Editor Settings and restart; confirm the density persists;
6. open a project, start `▶S`, add `▶C`, stop all, build, publish, and inspect Console output;
7. switch sessions and confirm Hierarchy/Inspector still follow the selected backend;
8. verify no new files appear under the game Content directory and no runtime process lifecycle changes occur.

- [x] **Step 4: Record results in the living plan**

Update `Progress`, `Surprises & Discoveries`, `Decision Log`, and `Outcomes & Retrospective` with command output summaries and any layout compatibility findings before closing the plan.

- [x] **Step 5: Commit final documentation updates**

```powershell
git add docs/04_Roadmap/kanban-0.6-editor.md docs/superpowers/plans/2026-09-20-editor-unity-like-workspace.md
git commit -m "docs(editor): record Unity-like workspace validation"
```

## Milestones

1. Workspace settings round-trip with legacy defaults.
2. Unity/Debug preset trees and custom layout preservation pass targeted tests.
3. Editor Settings applies density and layout without restarting runtime sessions.
4. Compact graphite shell exposes the agreed menu, toolbar, and hotkeys.
5. Full editor test/build/manual acceptance passes and the roadmap/plan record the result.

## Validation and Acceptance

Acceptance is observable when all five milestones pass, `Karpik.Editor.Tests` is green, the editor builds with single-node MSBuild, the manual scenario preserves custom layouts, and server/client preview lifecycle behavior remains unchanged.

## Idempotence and Recovery

- Workspace and dock saves use the existing temporary-file-then-move pattern; rerunning a save replaces only the intended user settings file.
- Applying a preset always saves the current layout to the custom store before replacing the active layout.
- If a saved layout cannot be deserialized, keep the file for diagnosis, report the error, and fall back to generated Unity layout.
- If a new theme resource fails to load, remove only `EditorTheme.axaml` references and return to Avalonia Fluent defaults; runtime projects remain unaffected.
- No destructive command such as `git reset --hard` or deletion of user Content is part of this plan.

## Artifacts and Notes

- Design specification: `docs/superpowers/specs/2026-09-20-editor-unity-like-workspace-design.md`.
- Existing architecture decision: `docs/02_ADR/editor-desktop-stack.md`.
- Existing dock persistence: `Karpik.Editor/Docking/DockLayoutStore.cs`.
- The visual companion mockups are exploratory only; the compact final proportions in the specification are authoritative.
