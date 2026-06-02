using DCFApixels.DragonECS;
using ImGuiNET;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class ImGuiBeginSystem : ISystemBegin
{
    [DI] private IInputSource _inputSource = null!;
    [DI] private Time _time = null!;
    [DI] private ImGuiOverlayState _overlay = null!;
    [DI] private ImGuiRenderContext _imgui = null!;
    [DI] private InputCaptureState _inputCapture = null!;

    public void Begin()
    {
        IReadOnlyList<KeyEvent> keyEvents = _inputSource.KeyEvents;
        for (int i = 0; i < keyEvents.Count; i++)
        {
            KeyEvent keyEvent = keyEvents[i];
            if (keyEvent is { Key: Key.F1, Down: true })
            {
                _overlay.Toggle();
                break;
            }
        }

        if (!_overlay.Enabled)
        {
            _overlay.ClearCapture();
            _inputCapture.Clear();
            return;
        }

        _imgui.Update((float)_time.DeltaTime, _inputSource.Snapshot);

        ImGuiIOPtr io = ImGui.GetIO();
        bool wantsMouse = io.WantCaptureMouse;
        bool wantsKeyboard = io.WantCaptureKeyboard;
        bool wantsText = io.WantTextInput || wantsKeyboard;
        _overlay.SetCapture(wantsMouse, wantsKeyboard, wantsText);
        _inputCapture.Set(wantsMouse, wantsKeyboard, wantsText);
    }
}

public sealed class ImGuiDebugPanelSystem : ISystemRender
{
    [DI] private ImGuiOverlayState _overlay = null!;
    [DI] private IInputSource _inputSource = null!;
    [DI] private Time _time = null!;

    private string _text = string.Empty;

    public void Render()
    {
        if (!_overlay.Enabled)
        {
            return;
        }

        ImGui.Begin("Karpik Debug");
        ImGui.TextUnformatted("ImGui overlay is running.");
        ImGui.Text($"Frame dt: {_time.DeltaTime * 1000.0:0.00} ms");
        ImGui.Text($"Mouse: {_inputSource.MousePosition.X:0}, {_inputSource.MousePosition.Y:0}");
        ImGui.InputText("Text", ref _text, 128);
        ImGui.End();
    }
}
