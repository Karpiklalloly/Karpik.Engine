using DCFApixels.DragonECS;
using System.Diagnostics;
using ImGuiNET;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using NeoVeldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class ImGuiBeginSystem(
    IInputSource inputSource,
    ImGuiOverlayState overlay,
    IGraphicsBackend backend,
    InputCaptureState inputCapture)
    : ISystemMainThreadBegin
{
    public void MainThreadBegin()
    {
        IReadOnlyList<KeyEvent> keyEvents = inputSource.KeyEvents;
        for (int i = 0; i < keyEvents.Count; i++)
        {
            KeyEvent keyEvent = keyEvents[i];
            if (keyEvent is { Key: Key.F1, Down: true })
            {
                overlay.Toggle();
                break;
            }
        }

        if (!overlay.Enabled)
        {
            overlay.ClearCapture();
            inputCapture.Clear();
            return;
        }

        backend.UpdateImGui(out bool wantsMouse, out bool wantsKeyboard, out bool wantsText);
        overlay.SetCapture(wantsMouse, wantsKeyboard, wantsText);
        inputCapture.Set(wantsMouse, wantsKeyboard, wantsText);
    }
}

public sealed class ImGuiDebugPanelSystem(
    ImGuiOverlayState overlay,
    IInputSource inputSource,
    Time time,
    ClientFrameMetrics clientFrameMetrics,
    GraphicsLoadTestSettings graphicsLoadTestSettings)
    : ISystemRender
{
    private string _text = string.Empty;

    public void Render()
    {
        if (!overlay.Enabled)
        {
            return;
        }

        ImGui.Begin("Karpik Debug");
        ImGui.TextUnformatted("ImGui overlay is running.");
        ImGui.Text($"Frame dt: {time.DeltaTime * 1000.0:0.00} ms");
        ImGui.Text($"FPS: {1 / time.DeltaTime:0}");
        ImGui.Text($"Mouse: {inputSource.MousePosition.X:0}, {inputSource.MousePosition.Y:0}");
        ClientFrameTimingSnapshot timings = clientFrameMetrics.GetSnapshot();
        ImGui.SeparatorText("Threaded frame timings");
        ImGui.TextUnformatted("Last completed 1-second window: avg / p95 / p99 / max");
        DrawTimingSummary("Main frame", timings.MainThreadFrame);
        DrawTimingSummary("Main begin", timings.MainThreadBegin);
        DrawTimingSummary("Frame begin", timings.MainThreadFrameBegin);
        DrawTimingSummary("Render", timings.Render);
        DrawTimingSummary("Simulation queue", timings.SimulationQueue);
        DrawTimingSummary("Simulation", timings.Simulation);
        DrawTimingSummary("Merge build", timings.MergeBuild);
        DrawTimingSummary("Merge sort", timings.MergeSort);
        DrawTimingSummary("Merge vertices", timings.MergeVertices);
        DrawTimingSummary("Merge buffer update", timings.MergeBufferUpdate);
        DrawTimingSummary("Merge draw encode", timings.MergeDrawEncode);
        DrawAllocationSummary("Merge allocations", timings.MergeAllocations);
        DrawTimingSummary("Present CPU", timings.PresentCpu);
        DrawTimingSummary("Present interval", timings.PresentInterval);
        ImGui.Text($"Present 1% low: {ToFramesPerSecond(timings.PresentInterval.P99Ticks):0} FPS");
        ImGui.Text($"Merge command list unavailable: {timings.MergeAvailability.UnavailableCount} / {timings.MergeAvailability.PollCount} ({GetPercent(timings.MergeAvailability):0.0}%)");
        if (ImGui.Button("Copy timings"))
        {
            ImGui.SetClipboardText(FrameTimingClipboardFormatter.Format(timings, graphicsLoadTestSettings.QuadCount, graphicsLoadTestSettings.Scenario));
        }
        int stressQuadCount = graphicsLoadTestSettings.QuadCount;
        if (ImGui.SliderInt("Stress quads", ref stressQuadCount, 0, GraphicsLoadTestSettings.MaxQuadCount))
        {
            graphicsLoadTestSettings.QuadCount = stressQuadCount;
        }
        if (ImGui.Button("Stress 1k")) graphicsLoadTestSettings.QuadCount = 1024;
        ImGui.SameLine();
        if (ImGui.Button("Stress 4k")) graphicsLoadTestSettings.QuadCount = 4096;
        ImGui.SameLine();
        if (ImGui.Button("Stress max")) graphicsLoadTestSettings.QuadCount = GraphicsLoadTestSettings.MaxQuadCount;
        ImGui.SameLine();
        if (ImGui.Button("Clear stress")) graphicsLoadTestSettings.QuadCount = 0;
        ImGui.Text($"Stress scenario: {GraphicsLoadTestSettings.GetScenarioName(graphicsLoadTestSettings.Scenario)}");
        if (ImGui.Button("Sorted rects")) graphicsLoadTestSettings.Scenario = GraphicsLoadTestScenario.SortedRects;
        ImGui.SameLine();
        if (ImGui.Button("Unsorted rects")) graphicsLoadTestSettings.Scenario = GraphicsLoadTestScenario.UnsortedRects;
        ImGui.SameLine();
        if (ImGui.Button("Texture batches")) graphicsLoadTestSettings.Scenario = GraphicsLoadTestScenario.TextureBatches;
        ImGui.SameLine();
        if (ImGui.Button("Texture thrash")) graphicsLoadTestSettings.Scenario = GraphicsLoadTestScenario.TextureThrash;
        ImGui.InputText("Text", ref _text, 128);
        ImGui.End();
    }

    private static void DrawTimingSummary(string name, TimingSummary summary)
    {
        ImGui.Text($"{name}: {ClientFrameMetrics.ToMilliseconds(summary.AverageTicks):0.00} / {ClientFrameMetrics.ToMilliseconds(summary.P95Ticks):0.00} / {ClientFrameMetrics.ToMilliseconds(summary.P99Ticks):0.00} / {ClientFrameMetrics.ToMilliseconds(summary.MaxTicks):0.00} ms ({summary.SampleCount})");
    }

    private static double GetPercent(MergeAvailabilitySummary summary)
    {
        return summary.PollCount == 0 ? 0d : summary.UnavailableCount * 100d / summary.PollCount;
    }

    private static void DrawAllocationSummary(string name, AllocationSummary summary)
    {
        ImGui.Text($"{name}: {summary.TotalBytes} B total / {summary.MaxBytes} B max, Gen0: {summary.Gen0CollectionCount} ({summary.SampleCount})");
    }

    private static double ToFramesPerSecond(long frameIntervalTicks)
    {
        return frameIntervalTicks == 0 ? 0d : Stopwatch.Frequency / (double)frameIntervalTicks;
    }
}
