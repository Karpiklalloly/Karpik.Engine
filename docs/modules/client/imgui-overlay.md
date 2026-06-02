# ImGui Overlay

`Graphics.Core` owns a client-only ImGui overlay for debug and temporary UI.
It is not the production gameplay UI path.

## Runtime

- Press `F1` to toggle the overlay.
- Set `KARPIK_IMGUI_ENABLED=1` before launching the client to start with the overlay enabled for deterministic local validation.
- `ImGuiBeginSystem` updates ImGui from the raw `IInputSource.Snapshot`.
- ECS systems can call `ImGuiNET.ImGui` after `ImGuiBeginSystem` and before `ImGuiRenderSystem`.
- `ImGuiRenderSystem` submits the ImGui command list after the scene command list.
- `GraphicsCoreSwapBuffersSystem` swaps buffers once at the end of the frame.

Current graphics ordering:

```text
BeginProgramLayer -2000  Window input pump
BeginProgramLayer -1800  Graphics begin frame and resize
BeginProgramLayer -1750  ImGui begin/update/capture
BeginProgramLayer -1700  Scene merge starts
Basic layer              Gameplay and render command systems
EndProgramLayer 1700     Scene command list submit
EndProgramLayer 1740     Built-in debug ImGui panel
EndProgramLayer 1750     ImGui command list submit
EndProgramLayer 1800     SwapBuffers
```

## Writing A Panel

Use direct ECS systems. Guard every panel with `ImGuiOverlayState.Enabled`.

```csharp
using ImGuiNET;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;

public sealed class MyDebugPanelSystem : ISystemRender
{
    [DI] private ImGuiOverlayState _overlay = null!;

    private string _text = string.Empty;

    public void Render()
    {
        if (!_overlay.Enabled)
        {
            return;
        }

        ImGui.Begin("My Debug Panel");
        ImGui.TextUnformatted("Hello from ImGui");
        ImGui.InputText("Text", ref _text, 128);
        ImGui.End();
    }
}
```

Register pure ImGui panels between the built-in debug panel and render system:

```csharp
b.Add(new MyDebugPanelSystem(), CustomLayers.END_PROGRAM_LAYER, 1745);
```

If a system also writes to `GraphicsContext.Buffer`, keep it in the normal gameplay/basic layer.
That lets the merge thread collect scene draw commands before `GraphicsCoreSubmitSceneSystem`.

## Input Capture

`InputCaptureState` is set from ImGui IO capture flags while the overlay is enabled.
Gameplay input reads this state and suppresses mouse, keyboard, and text input when ImGui wants capture.

Raw `IInputSource` remains unchanged so ImGui still receives the original Veldrid input snapshot.

## Constraints

- Client only. Do not reference ImGui from Server or Shared projects.
- Do not call ImGui APIs from the merge thread.
- Do not store gameplay UI state in ImGui systems.
- ImGui may allocate while enabled; keep it debug/tooling only.
