using System.Diagnostics;

namespace Karpik.Engine.Core;

public readonly struct ClientFrameTimingSnapshot
{
    public readonly long MainThreadFrameTicks;
    public readonly long MainThreadBeginTicks;
    public readonly long MainThreadFrameBeginTicks;
    public readonly long RenderTicks;
    public readonly long SimulationQueueTicks;
    public readonly long SimulationTicks;
    public readonly long MergeBuildTicks;
    public readonly bool HasCompletedMerge;

    internal ClientFrameTimingSnapshot(long mainThreadFrameTicks, long mainThreadBeginTicks, long mainThreadFrameBeginTicks, long renderTicks, long simulationQueueTicks, long simulationTicks, long mergeBuildTicks, bool hasCompletedMerge)
    {
        MainThreadFrameTicks = mainThreadFrameTicks;
        MainThreadBeginTicks = mainThreadBeginTicks;
        MainThreadFrameBeginTicks = mainThreadFrameBeginTicks;
        RenderTicks = renderTicks;
        SimulationQueueTicks = simulationQueueTicks;
        SimulationTicks = simulationTicks;
        MergeBuildTicks = mergeBuildTicks;
        HasCompletedMerge = hasCompletedMerge;
    }
}

public sealed class ClientFrameMetrics
{
    private long _mainThreadFrameTicks;
    private long _mainThreadBeginTicks;
    private long _mainThreadFrameBeginTicks;
    private long _renderTicks;
    private long _simulationQueueTicks;
    private long _simulationTicks;
    private long _mergeBuildTicks;
    private int _hasCompletedMerge;

    public void PublishMainThreadFrame(long ticks) => Volatile.Write(ref _mainThreadFrameTicks, ticks);
    public void PublishMainThreadBegin(long ticks) => Volatile.Write(ref _mainThreadBeginTicks, ticks);
    public void PublishMainThreadFrameBegin(long ticks) => Volatile.Write(ref _mainThreadFrameBeginTicks, ticks);
    public void PublishRender(long ticks) => Volatile.Write(ref _renderTicks, ticks);
    public void PublishSimulation(long queueTicks, long ticks) { Volatile.Write(ref _simulationQueueTicks, queueTicks); Volatile.Write(ref _simulationTicks, ticks); }
    public void PublishMergeBuild(long ticks) => Volatile.Write(ref _mergeBuildTicks, ticks);
    public void PublishMergeAvailability(bool isReady) => Volatile.Write(ref _hasCompletedMerge, isReady ? 1 : 0);

    public ClientFrameTimingSnapshot GetSnapshot() => new(
        Volatile.Read(ref _mainThreadFrameTicks), Volatile.Read(ref _mainThreadBeginTicks),
        Volatile.Read(ref _mainThreadFrameBeginTicks), Volatile.Read(ref _renderTicks),
        Volatile.Read(ref _simulationQueueTicks), Volatile.Read(ref _simulationTicks),
        Volatile.Read(ref _mergeBuildTicks), Volatile.Read(ref _hasCompletedMerge) != 0);

    public static double ToMilliseconds(long ticks) => ticks * (1000.0 / Stopwatch.Frequency);
}
