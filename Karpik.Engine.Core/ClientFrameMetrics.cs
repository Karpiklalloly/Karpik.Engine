using System.Diagnostics;

namespace Karpik.Engine.Core;

public readonly struct TimingSummary
{
    public readonly long AverageTicks;
    public readonly long P95Ticks;
    public readonly long P99Ticks;
    public readonly long MaxTicks;
    public readonly int SampleCount;

    internal TimingSummary(long averageTicks, long p95Ticks, long p99Ticks, long maxTicks, int sampleCount)
    {
        AverageTicks = averageTicks;
        P95Ticks = p95Ticks;
        P99Ticks = p99Ticks;
        MaxTicks = maxTicks;
        SampleCount = sampleCount;
    }
}

public readonly struct ClientFrameTimingSnapshot
{
    public readonly TimingSummary MainThreadFrame;
    public readonly TimingSummary MainThreadBegin;
    public readonly TimingSummary MainThreadFrameBegin;
    public readonly TimingSummary Render;
    public readonly TimingSummary SimulationQueue;
    public readonly TimingSummary Simulation;
    public readonly TimingSummary MergeBuild;
    public readonly TimingSummary PresentCpu;
    public readonly TimingSummary PresentInterval;
    public readonly MergeAvailabilitySummary MergeAvailability;

    internal ClientFrameTimingSnapshot(
        TimingSummary mainThreadFrame,
        TimingSummary mainThreadBegin,
        TimingSummary mainThreadFrameBegin,
        TimingSummary render,
        TimingSummary simulationQueue,
        TimingSummary simulation,
        TimingSummary mergeBuild,
        TimingSummary presentCpu,
        TimingSummary presentInterval,
        MergeAvailabilitySummary mergeAvailability)
    {
        MainThreadFrame = mainThreadFrame;
        MainThreadBegin = mainThreadBegin;
        MainThreadFrameBegin = mainThreadFrameBegin;
        Render = render;
        SimulationQueue = simulationQueue;
        Simulation = simulation;
        MergeBuild = mergeBuild;
        PresentCpu = presentCpu;
        PresentInterval = presentInterval;
        MergeAvailability = mergeAvailability;
    }
}

public readonly struct MergeAvailabilitySummary
{
    public readonly int UnavailableCount;
    public readonly int PollCount;

    internal MergeAvailabilitySummary(int unavailableCount, int pollCount)
    {
        UnavailableCount = unavailableCount;
        PollCount = pollCount;
    }
}

internal sealed class FrameTimingWindow
{
    private const int MaxSamples = 1024;

    private readonly long _windowDurationTicks;
    private readonly long[] _samples = new long[MaxSamples];
    private readonly long[] _sortedSamples = new long[MaxSamples];
    private long _windowStartedAt = -1;
    private long _sum;
    private long _max;
    private int _sampleCount;
    private int _totalSampleCount;
    private int _sampleWriteIndex;
    private long _averageTicks;
    private long _p95Ticks;
    private long _p99Ticks;
    private long _maxTicks;
    private int _publishedSampleCount;

    public FrameTimingWindow() : this(Stopwatch.Frequency)
    {
    }

    public FrameTimingWindow(long windowDurationTicks)
    {
        _windowDurationTicks = windowDurationTicks;
    }

    public void Publish(long ticks, long timestamp)
    {
        if (_windowStartedAt < 0)
        {
            _windowStartedAt = timestamp;
        }
        else if (timestamp - _windowStartedAt >= _windowDurationTicks)
        {
            PublishWindow();
            Reset(timestamp);
        }

        _sum += ticks;
        if (ticks > _max)
        {
            _max = ticks;
        }

        _samples[_sampleWriteIndex] = ticks;
        _sampleWriteIndex = (_sampleWriteIndex + 1) % MaxSamples;
        if (_sampleCount < MaxSamples)
        {
            _sampleCount++;
        }
        _totalSampleCount++;
    }

    public TimingSummary GetSummary() => new(
        Volatile.Read(ref _averageTicks),
        Volatile.Read(ref _p95Ticks),
        Volatile.Read(ref _p99Ticks),
        Volatile.Read(ref _maxTicks),
        Volatile.Read(ref _publishedSampleCount));

    private void PublishWindow()
    {
        if (_sampleCount == 0)
        {
            return;
        }

        Array.Copy(_samples, _sortedSamples, _sampleCount);
        Array.Sort(_sortedSamples, 0, _sampleCount);
        int p95Index = ((_sampleCount * 95 + 99) / 100) - 1;
        int p99Index = ((_sampleCount * 99 + 99) / 100) - 1;
        Volatile.Write(ref _averageTicks, _sum / _totalSampleCount);
        Volatile.Write(ref _p95Ticks, _sortedSamples[p95Index]);
        Volatile.Write(ref _p99Ticks, _sortedSamples[p99Index]);
        Volatile.Write(ref _maxTicks, _max);
        Volatile.Write(ref _publishedSampleCount, _totalSampleCount);
    }

    private void Reset(long timestamp)
    {
        _windowStartedAt = timestamp;
        _sum = 0;
        _max = 0;
        _sampleCount = 0;
        _totalSampleCount = 0;
        _sampleWriteIndex = 0;
    }
}

internal sealed class FrameCounterWindow
{
    private readonly long _windowDurationTicks;
    private long _windowStartedAt = -1;
    private int _unavailableCount;
    private int _pollCount;
    private int _publishedUnavailableCount;
    private int _publishedPollCount;

    public FrameCounterWindow() : this(Stopwatch.Frequency)
    {
    }

    public FrameCounterWindow(long windowDurationTicks)
    {
        _windowDurationTicks = windowDurationTicks;
    }

    public MergeAvailabilitySummary GetSummary() => new(
        Volatile.Read(ref _publishedUnavailableCount),
        Volatile.Read(ref _publishedPollCount));

    public void Publish(bool isAvailable, long timestamp)
    {
        if (_windowStartedAt < 0)
        {
            _windowStartedAt = timestamp;
        }
        else if (timestamp - _windowStartedAt >= _windowDurationTicks)
        {
            Volatile.Write(ref _publishedUnavailableCount, _unavailableCount);
            Volatile.Write(ref _publishedPollCount, _pollCount);
            _windowStartedAt = timestamp;
            _unavailableCount = 0;
            _pollCount = 0;
        }

        _pollCount++;

        if (!isAvailable)
        {
            _unavailableCount++;
        }
    }
}

public sealed class ClientFrameMetrics
{
    private readonly FrameTimingWindow _mainThreadFrame = new();
    private readonly FrameTimingWindow _mainThreadBegin = new();
    private readonly FrameTimingWindow _mainThreadFrameBegin = new();
    private readonly FrameTimingWindow _render = new();
    private readonly FrameTimingWindow _simulationQueue = new();
    private readonly FrameTimingWindow _simulation = new();
    private readonly FrameTimingWindow _mergeBuild = new();
    private readonly FrameTimingWindow _presentCpu = new();
    private readonly FrameTimingWindow _presentInterval = new();
    private readonly FrameCounterWindow _mergeAvailability = new();
    private long _lastPresentTimestamp = -1;

    public void PublishMainThreadFrame(long ticks) => _mainThreadFrame.Publish(ticks, Stopwatch.GetTimestamp());
    public void PublishMainThreadBegin(long ticks) => _mainThreadBegin.Publish(ticks, Stopwatch.GetTimestamp());
    public void PublishMainThreadFrameBegin(long ticks) => _mainThreadFrameBegin.Publish(ticks, Stopwatch.GetTimestamp());
    public void PublishRender(long ticks) => _render.Publish(ticks, Stopwatch.GetTimestamp());

    public void PublishSimulation(long queueTicks, long ticks)
    {
        long timestamp = Stopwatch.GetTimestamp();
        _simulationQueue.Publish(queueTicks, timestamp);
        _simulation.Publish(ticks, timestamp);
    }

    public void PublishMergeBuild(long ticks) => _mergeBuild.Publish(ticks, Stopwatch.GetTimestamp());
    public void PublishMergeAvailability(bool isReady) => _mergeAvailability.Publish(isReady, Stopwatch.GetTimestamp());

    public void PublishPresent(long cpuTicks)
    {
        long timestamp = Stopwatch.GetTimestamp();
        _presentCpu.Publish(cpuTicks, timestamp);
        long previousTimestamp = Interlocked.Exchange(ref _lastPresentTimestamp, timestamp);
        if (previousTimestamp >= 0)
        {
            _presentInterval.Publish(timestamp - previousTimestamp, timestamp);
        }
    }

    public ClientFrameTimingSnapshot GetSnapshot() => new(
        _mainThreadFrame.GetSummary(),
        _mainThreadBegin.GetSummary(),
        _mainThreadFrameBegin.GetSummary(),
        _render.GetSummary(),
        _simulationQueue.GetSummary(),
        _simulation.GetSummary(),
        _mergeBuild.GetSummary(),
        _presentCpu.GetSummary(),
        _presentInterval.GetSummary(),
        _mergeAvailability.GetSummary());

    public static double ToMilliseconds(long ticks) => ticks * (1000.0 / Stopwatch.Frequency);
}
