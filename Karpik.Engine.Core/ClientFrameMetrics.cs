using System.Diagnostics;
using System.Numerics;

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
    public readonly TimingSummary MergeSort;
    public readonly TimingSummary MergeVertices;
    public readonly TimingSummary MergeBufferUpdate;
    public readonly TimingSummary MergeDrawEncode;
    public readonly AllocationSummary MergeAllocations;
    public readonly TimingSummary PresentCpu;
    public readonly TimingSummary PresentInterval;
    public readonly TimingSummary GpuCommand;
    public readonly MergeAvailabilitySummary MergeAvailability;

    internal ClientFrameTimingSnapshot(
        TimingSummary mainThreadFrame,
        TimingSummary mainThreadBegin,
        TimingSummary mainThreadFrameBegin,
        TimingSummary render,
        TimingSummary simulationQueue,
        TimingSummary simulation,
        TimingSummary mergeBuild,
        TimingSummary mergeSort,
        TimingSummary mergeVertices,
        TimingSummary mergeBufferUpdate,
        TimingSummary mergeDrawEncode,
        AllocationSummary mergeAllocations,
        TimingSummary presentCpu,
        TimingSummary presentInterval,
        TimingSummary gpuCommand,
        MergeAvailabilitySummary mergeAvailability)
    {
        MainThreadFrame = mainThreadFrame;
        MainThreadBegin = mainThreadBegin;
        MainThreadFrameBegin = mainThreadFrameBegin;
        Render = render;
        SimulationQueue = simulationQueue;
        Simulation = simulation;
        MergeBuild = mergeBuild;
        MergeSort = mergeSort;
        MergeVertices = mergeVertices;
        MergeBufferUpdate = mergeBufferUpdate;
        MergeDrawEncode = mergeDrawEncode;
        MergeAllocations = mergeAllocations;
        PresentCpu = presentCpu;
        PresentInterval = presentInterval;
        GpuCommand = gpuCommand;
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

public readonly struct AllocationSummary
{
    public readonly long TotalBytes;
    public readonly long MaxBytes;
    public readonly int Gen0CollectionCount;
    public readonly int SampleCount;

    internal AllocationSummary(long totalBytes, long maxBytes, int gen0CollectionCount, int sampleCount)
    {
        TotalBytes = totalBytes;
        MaxBytes = maxBytes;
        Gen0CollectionCount = gen0CollectionCount;
        SampleCount = sampleCount;
    }
}

internal sealed class FrameTimingWindow
{
    // 64 buckets per power-of-two interval gives percentile precision better than 1.6%
    // while keeping the hot path allocation-free and bounded.
    private const int BucketsPerPowerOfTwo = 64;
    private const int ZeroBucket = 0;
    private const int HistogramBucketCount = sizeof(long) * 8 * BucketsPerPowerOfTwo + 1;

    private readonly long _windowDurationTicks;
    private readonly int[] _histogram = new int[HistogramBucketCount];
    private long _windowStartedAt = -1;
    private long _sum;
    private long _max;
    private int _totalSampleCount;
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

        _histogram[GetHistogramBucket(ticks)]++;
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
        if (_totalSampleCount == 0)
        {
            return;
        }

        Volatile.Write(ref _averageTicks, _sum / _totalSampleCount);
        Volatile.Write(ref _p95Ticks, Math.Min(_max, GetPercentileTicks(95)));
        Volatile.Write(ref _p99Ticks, Math.Min(_max, GetPercentileTicks(99)));
        Volatile.Write(ref _maxTicks, _max);
        Volatile.Write(ref _publishedSampleCount, _totalSampleCount);
    }

    private void Reset(long timestamp)
    {
        _windowStartedAt = timestamp;
        _sum = 0;
        _max = 0;
        _totalSampleCount = 0;
        Array.Clear(_histogram);
    }

    private static int GetHistogramBucket(long ticks)
    {
        if (ticks <= 0)
        {
            return ZeroBucket;
        }

        ulong value = (ulong)ticks;
        int exponent = BitOperations.Log2(value);
        ulong baseValue = 1UL << exponent;
        int subdivision = (int)(((value - baseValue) * BucketsPerPowerOfTwo) / baseValue);
        return Math.Min(1 + exponent * BucketsPerPowerOfTwo + subdivision, HistogramBucketCount - 1);
    }

    private long GetPercentileTicks(int percentile)
    {
        int targetSampleCount = (_totalSampleCount * percentile + 99) / 100;
        int accumulatedSampleCount = 0;
        for (int bucket = 0; bucket < HistogramBucketCount; bucket++)
        {
            accumulatedSampleCount += _histogram[bucket];
            if (accumulatedSampleCount >= targetSampleCount)
            {
                return GetBucketUpperBound(bucket);
            }
        }

        return _max;
    }

    private static long GetBucketUpperBound(int bucket)
    {
        if (bucket == ZeroBucket)
        {
            return 0;
        }

        int adjustedBucket = bucket - 1;
        int exponent = adjustedBucket / BucketsPerPowerOfTwo;
        int subdivision = adjustedBucket % BucketsPerPowerOfTwo;
        ulong baseValue = 1UL << exponent;
        ulong upperBound = baseValue + ((baseValue * (uint)(subdivision + 1)) / BucketsPerPowerOfTwo);
        return upperBound > long.MaxValue ? long.MaxValue : (long)upperBound;
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

internal sealed class FrameAllocationWindow
{
    private readonly long _windowDurationTicks;
    private long _windowStartedAt = -1;
    private long _totalBytes;
    private long _maxBytes;
    private int _gen0CollectionCount;
    private int _sampleCount;
    private long _publishedTotalBytes;
    private long _publishedMaxBytes;
    private int _publishedGen0CollectionCount;
    private int _publishedSampleCount;

    public FrameAllocationWindow() : this(Stopwatch.Frequency)
    {
    }

    public FrameAllocationWindow(long windowDurationTicks)
    {
        _windowDurationTicks = windowDurationTicks;
    }

    public void Publish(long bytes, int gen0CollectionCount, long timestamp)
    {
        if (_windowStartedAt < 0)
        {
            _windowStartedAt = timestamp;
        }
        else if (timestamp - _windowStartedAt >= _windowDurationTicks)
        {
            Volatile.Write(ref _publishedTotalBytes, _totalBytes);
            Volatile.Write(ref _publishedMaxBytes, _maxBytes);
            Volatile.Write(ref _publishedGen0CollectionCount, _gen0CollectionCount);
            Volatile.Write(ref _publishedSampleCount, _sampleCount);
            _windowStartedAt = timestamp;
            _totalBytes = 0;
            _maxBytes = 0;
            _gen0CollectionCount = 0;
            _sampleCount = 0;
        }

        _totalBytes += bytes;
        if (bytes > _maxBytes)
        {
            _maxBytes = bytes;
        }

        _gen0CollectionCount += gen0CollectionCount;
        _sampleCount++;
    }

    public AllocationSummary GetSummary() => new(
        Volatile.Read(ref _publishedTotalBytes),
        Volatile.Read(ref _publishedMaxBytes),
        Volatile.Read(ref _publishedGen0CollectionCount),
        Volatile.Read(ref _publishedSampleCount));
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
    private readonly FrameTimingWindow _mergeSort = new();
    private readonly FrameTimingWindow _mergeVertices = new();
    private readonly FrameTimingWindow _mergeBufferUpdate = new();
    private readonly FrameTimingWindow _mergeDrawEncode = new();
    private readonly FrameAllocationWindow _mergeAllocations = new();
    private readonly FrameTimingWindow _presentCpu = new();
    private readonly FrameTimingWindow _presentInterval = new();
    private readonly FrameTimingWindow _gpuCommand = new();
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
    public void PublishMergeBreakdown(long sortTicks, long vertexTicks, long bufferUpdateTicks, long drawEncodeTicks, long allocatedBytes, int gen0Collections)
    {
        long timestamp = Stopwatch.GetTimestamp();
        _mergeSort.Publish(sortTicks, timestamp);
        _mergeVertices.Publish(vertexTicks, timestamp);
        _mergeBufferUpdate.Publish(bufferUpdateTicks, timestamp);
        _mergeDrawEncode.Publish(drawEncodeTicks, timestamp);
        _mergeAllocations.Publish(allocatedBytes, gen0Collections, timestamp);
    }
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

    public void PublishGpuCommandNanoseconds(long nanoseconds)
    {
        _gpuCommand.Publish(NanosecondsToStopwatchTicks(nanoseconds), Stopwatch.GetTimestamp());
    }

    public ClientFrameTimingSnapshot GetSnapshot() => new(
        _mainThreadFrame.GetSummary(),
        _mainThreadBegin.GetSummary(),
        _mainThreadFrameBegin.GetSummary(),
        _render.GetSummary(),
        _simulationQueue.GetSummary(),
        _simulation.GetSummary(),
        _mergeBuild.GetSummary(),
        _mergeSort.GetSummary(),
        _mergeVertices.GetSummary(),
        _mergeBufferUpdate.GetSummary(),
        _mergeDrawEncode.GetSummary(),
        _mergeAllocations.GetSummary(),
        _presentCpu.GetSummary(),
        _presentInterval.GetSummary(),
        _gpuCommand.GetSummary(),
        _mergeAvailability.GetSummary());

    public static double ToMilliseconds(long ticks) => ticks * (1000.0 / Stopwatch.Frequency);

    public static long NanosecondsToStopwatchTicks(long nanoseconds)
    {
        return (long)(nanoseconds * (double)Stopwatch.Frequency / 1_000_000_000d);
    }
}
