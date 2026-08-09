using System.Diagnostics;
using System.Text;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

public static class FrameTimingClipboardFormatter
{
    public static string Format(in ClientFrameTimingSnapshot timings, int stressQuadCount, GraphicsLoadTestScenario stressScenario)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("Karpik threaded frame timings");
        builder.AppendLine("Last completed 1-second window: avg / p95 / p99 / max (ms)");
        AppendTiming(builder, "Main frame", timings.MainThreadFrame);
        AppendTiming(builder, "Main begin", timings.MainThreadBegin);
        AppendTiming(builder, "Frame begin", timings.MainThreadFrameBegin);
        AppendTiming(builder, "Render", timings.Render);
        AppendTiming(builder, "Simulation queue", timings.SimulationQueue);
        AppendTiming(builder, "Simulation", timings.Simulation);
        AppendTiming(builder, "Merge build", timings.MergeBuild);
        AppendTiming(builder, "Merge sort", timings.MergeSort);
        AppendTiming(builder, "Merge vertices", timings.MergeVertices);
        AppendTiming(builder, "Merge buffer update", timings.MergeBufferUpdate);
        AppendTiming(builder, "Merge draw encode", timings.MergeDrawEncode);
        AppendAllocation(builder, "Merge allocations", timings.MergeAllocations);
        AppendTiming(builder, "Present CPU", timings.PresentCpu);
        AppendTiming(builder, "Present interval", timings.PresentInterval);
        double onePercentLowFps = timings.PresentInterval.P99Ticks == 0 ? 0d : Stopwatch.Frequency / (double)timings.PresentInterval.P99Ticks;
        builder.Append("Present 1% low: ").Append(onePercentLowFps.ToString("0")).AppendLine(" FPS");
        MergeAvailabilitySummary availability = timings.MergeAvailability;
        double unavailablePercent = availability.PollCount == 0 ? 0d : availability.UnavailableCount * 100d / availability.PollCount;
        builder.Append("Merge command list unavailable: ")
            .Append(availability.UnavailableCount).Append(" / ").Append(availability.PollCount)
            .Append(" (").Append(unavailablePercent.ToString("0.0")).AppendLine("%)");
        builder.Append("Stress quads: ").AppendLine(stressQuadCount.ToString());
        builder.Append("Stress scenario: ").Append(GraphicsLoadTestSettings.GetScenarioName(stressScenario));
        return builder.ToString();
    }

    private static void AppendTiming(StringBuilder builder, string name, TimingSummary summary)
    {
        builder.Append(name).Append(": ")
            .Append(ClientFrameMetrics.ToMilliseconds(summary.AverageTicks).ToString("0.00")).Append(" / ")
            .Append(ClientFrameMetrics.ToMilliseconds(summary.P95Ticks).ToString("0.00")).Append(" / ")
            .Append(ClientFrameMetrics.ToMilliseconds(summary.P99Ticks).ToString("0.00")).Append(" / ")
            .Append(ClientFrameMetrics.ToMilliseconds(summary.MaxTicks).ToString("0.00"))
            .Append(" ms (").Append(summary.SampleCount).AppendLine(")");
    }

    private static void AppendAllocation(StringBuilder builder, string name, AllocationSummary summary)
    {
        builder.Append(name).Append(": ")
            .Append(summary.TotalBytes).Append(" B total / ")
            .Append(summary.MaxBytes).Append(" B max, Gen0: ")
            .Append(summary.Gen0CollectionCount).Append(" (").Append(summary.SampleCount).AppendLine(")");
    }
}
