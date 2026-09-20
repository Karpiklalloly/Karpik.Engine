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
