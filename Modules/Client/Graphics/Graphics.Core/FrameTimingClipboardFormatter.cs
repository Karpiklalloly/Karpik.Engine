using System.Text;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

public static class FrameTimingClipboardFormatter
{
    public static string Format(in ClientFrameTimingSnapshot timings, int stressQuadCount)
    {
        var builder = new StringBuilder(512);
        builder.AppendLine("Karpik threaded frame timings");
        builder.AppendLine("Last completed 1-second window: avg / p95 / max (ms)");
        AppendTiming(builder, "Main frame", timings.MainThreadFrame);
        AppendTiming(builder, "Main begin", timings.MainThreadBegin);
        AppendTiming(builder, "Frame begin", timings.MainThreadFrameBegin);
        AppendTiming(builder, "Render", timings.Render);
        AppendTiming(builder, "Simulation queue", timings.SimulationQueue);
        AppendTiming(builder, "Simulation", timings.Simulation);
        AppendTiming(builder, "Merge build", timings.MergeBuild);
        builder.Append("Merge command list unavailable: ").AppendLine(timings.MergeUnavailableCount.ToString());
        builder.Append("Stress quads: ").Append(stressQuadCount);
        return builder.ToString();
    }

    private static void AppendTiming(StringBuilder builder, string name, TimingSummary summary)
    {
        builder.Append(name).Append(": ")
            .Append(ClientFrameMetrics.ToMilliseconds(summary.AverageTicks).ToString("0.00")).Append(" / ")
            .Append(ClientFrameMetrics.ToMilliseconds(summary.P95Ticks).ToString("0.00")).Append(" / ")
            .Append(ClientFrameMetrics.ToMilliseconds(summary.MaxTicks).ToString("0.00"))
            .Append(" ms (").Append(summary.SampleCount).AppendLine(")");
    }
}
