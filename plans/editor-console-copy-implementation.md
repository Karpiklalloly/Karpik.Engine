# Editor Console Copy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Copy one complete selected editor-console row through `Ctrl+C` or a `Копировать` context-menu action.

**Architecture:** Keep platform clipboard access in `MainWindow`. A small pure async helper accepts the selected value and a clipboard-write delegate, so exact/no-selection/failure behavior is unit-testable without initializing Avalonia desktop services. The console remains a single-selection `ListBox`.

**Tech Stack:** C# 14, .NET 10, Avalonia 11, xUnit v3, ReactiveUI.

## Global Constraints

- Copy the displayed row without transforming timestamp, session prefix, or whitespace.
- `Ctrl+C` applies only while the console list has focus.
- Right-click selects the pointed row before its context menu opens.
- Missing selection, unavailable clipboard, or clipboard failure is a no-op.
- Do not add clipboard services to `ConsoleViewModel` or project/runtime lifetimes.

---

### Task 1: Testable console-copy contract

**Files:**
- Create: `Karpik.Editor/Clipboard/ConsoleMessageCopy.cs`
- Create: `Karpik.Editor.Tests/ConsoleMessageCopyTests.cs`

**Interfaces:**
- Produces: `ConsoleMessageCopy.TryCopyAsync(object? selectedItem, Func<string, Task>? writeTextAsync) -> Task<bool>`.

- [x] **Step 1: Write failing exact-text and no-selection tests**

```csharp
[Fact]
public async Task TryCopyAsync_SelectedMessage_WritesExactDisplayedText()
{
    string? copied = null;
    bool result = await ConsoleMessageCopy.TryCopyAsync(
        "[12:34:56] [Server] ready",
        text => { copied = text; return Task.CompletedTask; });
    Assert.True(result);
    Assert.Equal("[12:34:56] [Server] ready", copied);
}

[Fact]
public async Task TryCopyAsync_NoSelection_DoesNotWrite()
{
    int writes = 0;
    bool result = await ConsoleMessageCopy.TryCopyAsync(
        null,
        _ => { writes++; return Task.CompletedTask; });
    Assert.False(result);
    Assert.Equal(0, writes);
}
```

- [x] **Step 2: Run tests and verify RED**

Run: `dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --no-restore --filter ConsoleMessageCopyTests`

Expected: compilation fails because `ConsoleMessageCopy` does not exist.

- [x] **Step 3: Implement the minimal helper**

```csharp
public static class ConsoleMessageCopy
{
    public static async Task<bool> TryCopyAsync(
        object? selectedItem,
        Func<string, Task>? writeTextAsync)
    {
        if (selectedItem is not string message || writeTextAsync is null)
        {
            return false;
        }
        try
        {
            await writeTextAsync(message);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
```

- [x] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all `ConsoleMessageCopyTests` pass.

### Task 2: Avalonia gestures and row selection

**Files:**
- Modify: `Karpik.Editor/MainWindow.axaml` console `DataTemplate`.
- Modify: `Karpik.Editor/MainWindow.axaml.cs` event handlers.

**Interfaces:**
- Consumes: `ConsoleMessageCopy.TryCopyAsync` from Task 1.
- Produces: `Ctrl+C`, right-click row selection, and `Копировать` context-menu behavior.

- [x] **Step 1: Add console item template and gestures**

Add `KeyDown="ConsoleLog_OnKeyDown"` to the console `ListBox`. Give each row a `Border` with `PointerPressed="ConsoleMessage_OnPointerPressed"` and a context `MenuItem` whose `CommandParameter="{Binding}"` and `Click="CopyConsoleMessage_OnClick"`.

- [x] **Step 2: Add event handlers**

Implement keyboard detection with `Key.C` plus `KeyModifiers.Control`; select the ancestor `ListBoxItem` on right-button press; pass either `ListBox.SelectedItem` or `MenuItem.CommandParameter` to `ConsoleMessageCopy.TryCopyAsync` with `Clipboard?.SetTextAsync`.

- [x] **Step 3: Build XAML and run the focused tests**

Run: `dotnet build Karpik.Editor\Karpik.Editor.csproj -m:1 -nr:false --no-restore`

Expected: zero errors, and no unresolved XAML event handlers.

### Task 3: Acceptance and cache

**Files:**
- Update: `graphify-out/` through Graphify.

- [x] **Step 1: Run the full editor suite**

Run: `dotnet test Karpik.Editor.Tests\Karpik.Editor.Tests.csproj -m:1 -nr:false --no-restore`

Expected: all non-opt-in tests pass; the external-project switch remains skipped without its environment flag.

- [x] **Step 2: Check the diff**

Run: `git diff --check`.

Expected: exit code 0; line-ending notices are acceptable.

- [x] **Step 3: Update Graphify**

Run: `graphify update .`.

Expected: graph and report rebuild successfully and include `ConsoleMessageCopy`.
