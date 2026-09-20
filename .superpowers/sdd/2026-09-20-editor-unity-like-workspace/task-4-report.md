# Task 4 report: compact editor shell

## Status

Implemented and committed as `feat(editor): add compact Unity-like shell`.

## Changes

- Added `EditorTheme.axaml` and included it from `App.axaml`.
- Added graphite resources for `#1E1E1E`, `#252526`, `#2D2D30`, and `#3E6EAA`.
- Added compact density styles and a 20 px minimum height for interactive controls.
- Replaced the old Russian File/Run shell with `File`, `Edit`, `Assets`, `GameObject`, `Window`, and `Help` menus.
- Kept `Undo`, `Redo`, `Frame Selected`, and `Delete` disabled because no authoring/undo stack exists.
- Kept `Editor Settings…` wired to Task 3's existing `OpenEditorSettings` path.
- Added compact `▶S`, `▶C`, `■`, `Build`, `Publish`, and `Runtime` toolbar buttons using the existing shell command instances and `Can*` state bindings.
- Added native Avalonia menu hotkeys for open/save/save-as, undo/redo, frame/delete, and exit.
- Added workspace save and save-as handlers without changing runtime or project boundaries.
- Updated runtime command creation so `ReactiveCommand.CanExecute` forwards the existing `CanStartServer`, `CanAddClient`, `CanStopAll`, `CanBuild`, and `CanPublish` state.
- Added `EditorCommandPresentationTests` covering state forwarding before project load and after project activation.

## Self-review

`git diff --check` passed. The diff is limited to the Task 4 source/theme/XAML files, the command presentation test, and this report. No Client, Server, Shared, ECS, dependency, or command-bus changes were made. Existing Task 3 settings application and layout-selection paths remain referenced unchanged.

## Validation concerns

No build or test command was run after completing the implementation, per instruction. The earlier targeted test attempt was blocked before test discovery because the linked worktree could not write generated `obj` files (`Access denied`). The repository also has the known missing first-party `Karpik.Jobs` and `DragonECS.Karpik.Extensions` project files. The final compile/test result therefore remains unverified in this worktree.

## Review fix round

- Replaced the inaccessible private backend-factory reference in the presentation test with the public shell constructor and a small unused project-open service.
- Cast commands to `System.Windows.Input.ICommand` before calling `CanExecute(null)`.
- Removed unsupported server start/stop claims; the test now checks command state forwarding before and after the shell receives an active project context, without pretending to exercise a coordinator or runtime transition.
- Wrapped current-layout/workspace and save-as writes in filesystem-error handling that reports through `EditorShellViewModel.ReportEditorMessage`.
- Applied panel and accent brushes to editor panel roots and selected `ListBoxItem`/`TabItem` styles so all declared graphite resources are consumed.

## Final review fix wave

- Custom layouts are saved only when leaving an active `Custom` preset; Debug/Unity changes no longer overwrite the custom snapshot. Current layout is always restored/saved, while Debug startup restores a validated current layout before generating a fresh Debug tree.
- `EditorProjectLifetime.SaveWorkspaceAsync` now carries `UiDensity` and `LayoutPreset`; the existing lifetime persistence test asserts both fields.
- Added `EditorDockFactory.IsValidLayout` for required IDs/types and unknown dock IDs. Startup and settings application validate before attach/init and report/fallback through the existing editor message path.
- Debug-generated layouts now activate `Sessions` and `Console`; focused dock tests cover this and invalid-ID rejection.
- Extended density-specific sizing to `ListBoxItem`, `TreeViewItem`, `TabItem`, `TextBox`, and `ComboBox`, preserving the 20 px floor and 28 px Large sizing.

Validation remains intentionally unrun: no long dotnet commands were executed. The linked worktree's `obj` write-access failure and missing first-party `Karpik.Jobs`/`DragonECS.Karpik.Extensions` projects remain concerns.

## Scoped re-review fix wave

- Entering `Custom` now seeds the custom store from the active Unity/Debug layout only when neither the current nor legacy custom file exists; active Custom edits continue to save before switching away.
- Layout validation now collects visible, hidden, pinned, pinned-dock, and floating-window root dockables, while still rejecting unknown IDs, wrong types, and missing required IDs. The focused dock test covers hidden and pinned placement.
- Added density-specific `DocumentTabStripItem` and `ToolTabStripItem` selectors from `Dock.Avalonia`, including minimum height, padding, and font sizing.

No dotnet build/test commands were run for this wave. Existing worktree `obj` write-access and missing first-party project blockers remain recorded concerns.
