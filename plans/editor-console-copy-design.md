# Copy editor console messages

## Goal

Allow the user to copy one complete selected message from the editor console using either `Ctrl+C` or a `Копировать` context-menu item.

## Interaction contract

- The console remains a row-based `ListBox`.
- A copied value is the selected row exactly as displayed, including timestamp and session prefix.
- `Ctrl+C` works while the console list has keyboard focus.
- Right-clicking a row selects that row; choosing `Копировать` copies it.
- With no selected row, copying is a no-op.
- An unavailable clipboard or clipboard write failure must not terminate the editor or clear the current selection.

## Design

Clipboard access stays in the Avalonia view boundary rather than entering `ConsoleViewModel`. `MainWindow` handles the keyboard and context-menu gestures and delegates the actual text write to a small clipboard adapter/helper. This keeps the log model independent from platform UI services while leaving the copy operation independently testable.

No multi-row selection, substring selection, formatting transformation, notification toast, or automatic copying is introduced.

## Verification

- A unit test proves that the exact selected string is passed to the clipboard writer.
- A unit test proves that a missing selection performs no clipboard write.
- Avalonia compilation verifies the XAML event bindings.
- The full editor test project and editor build remain green.
