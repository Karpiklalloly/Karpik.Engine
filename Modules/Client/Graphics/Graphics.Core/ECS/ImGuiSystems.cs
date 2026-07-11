using DCFApixels.DragonECS;
using ImGuiNET;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class ImGuiBeginSystem : ISystemMainThreadBegin
{
    [DI] private IInputSource _inputSource = null!;
    [DI] private ImGuiOverlayState _overlay = null!;
    [DI] private IGraphicsBackend _backend = null!;
    [DI] private InputCaptureState _inputCapture = null!;

    public void MainThreadBegin()
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

        _backend.UpdateImGui(out bool wantsMouse, out bool wantsKeyboard, out bool wantsText);
        _overlay.SetCapture(wantsMouse, wantsKeyboard, wantsText);
        _inputCapture.Set(wantsMouse, wantsKeyboard, wantsText);
    }
}

public sealed class ImGuiDebugPanelSystem : ISystemRender
{
    [DI] private ImGuiOverlayState _overlay = null!;
    [DI] private IInputSource _inputSource = null!;
    [DI] private Time _time = null!;
    [DI] private ClientFrameMetrics _clientFrameMetrics = null!;
    [DI] private GraphicsLoadTestSettings _graphicsLoadTestSettings = null!;

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
        ImGui.Text($"FPS: {1 / _time.DeltaTime:0}");
        ImGui.Text($"Mouse: {_inputSource.MousePosition.X:0}, {_inputSource.MousePosition.Y:0}");
        ClientFrameTimingSnapshot timings = _clientFrameMetrics.GetSnapshot();
        ImGui.SeparatorText("Threaded frame timings");
        ImGui.TextUnformatted("Last completed 1-second window: avg / p95 / max");
        DrawTimingSummary("Main frame", timings.MainThreadFrame);
        DrawTimingSummary("Main begin", timings.MainThreadBegin);
        DrawTimingSummary("Frame begin", timings.MainThreadFrameBegin);
        DrawTimingSummary("Render", timings.Render);
        DrawTimingSummary("Simulation queue", timings.SimulationQueue);
        DrawTimingSummary("Simulation", timings.Simulation);
        DrawTimingSummary("Merge build", timings.MergeBuild);
        ImGui.Text($"Merge command list unavailable: {timings.MergeAvailability.UnavailableCount} / {timings.MergeAvailability.PollCount} ({GetPercent(timings.MergeAvailability):0.0}%)");
        if (ImGui.Button("Copy timings"))
        {
            ImGui.SetClipboardText(FrameTimingClipboardFormatter.Format(timings, _graphicsLoadTestSettings.QuadCount));
        }
        int stressQuadCount = _graphicsLoadTestSettings.QuadCount;
        if (ImGui.SliderInt("Stress quads", ref stressQuadCount, 0, GraphicsLoadTestSettings.MaxQuadCount))
        {
            _graphicsLoadTestSettings.QuadCount = stressQuadCount;
        }
        if (ImGui.Button("Stress 1k")) _graphicsLoadTestSettings.QuadCount = 1024;
        ImGui.SameLine();
        if (ImGui.Button("Stress 4k")) _graphicsLoadTestSettings.QuadCount = 4096;
        ImGui.SameLine();
        if (ImGui.Button("Stress 8k")) _graphicsLoadTestSettings.QuadCount = GraphicsLoadTestSettings.MaxQuadCount;
        ImGui.SameLine();
        if (ImGui.Button("Clear stress")) _graphicsLoadTestSettings.QuadCount = 0;
        ImGui.InputText("Text", ref _text, 128);
        ImGui.End();
    }

    private static void DrawTimingSummary(string name, TimingSummary summary)
    {
        ImGui.Text($"{name}: {ClientFrameMetrics.ToMilliseconds(summary.AverageTicks):0.00} / {ClientFrameMetrics.ToMilliseconds(summary.P95Ticks):0.00} / {ClientFrameMetrics.ToMilliseconds(summary.MaxTicks):0.00} ms ({summary.SampleCount})");
    }

    private static double GetPercent(MergeAvailabilitySummary summary)
    {
        return summary.PollCount == 0 ? 0d : summary.UnavailableCount * 100d / summary.PollCount;
    }
}
