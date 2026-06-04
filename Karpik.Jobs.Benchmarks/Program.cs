using System.Diagnostics;
using System.Globalization;
using Karpik.Jobs;

const int IndependentJobs = 10_000;
const int ChainJobs = 1_000;
const int ParallelItems = 32_768;
const int ParallelBatchSize = 64;
const int LatencySamples = 1_000;
const int WarmupJobs = 512;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

Console.WriteLine("Karpik.Jobs delegate baseline benchmark");
Console.WriteLine($"ProcessorCount: {Environment.ProcessorCount}");
Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
Console.WriteLine("ManagedBytes measures current-thread publication/allocation after warm-up.");

int[] workerCounts = BuildWorkerCounts();
for (int i = 0; i < workerCounts.Length; i++)
{
    int workerCount = workerCounts[i];
    Console.WriteLine();
    Console.WriteLine($"Workers: {workerCount}");

    RunIndependent(workerCount);
    RunDependencyChain(workerCount);
    RunParallelBatch(workerCount);
    RunRoundTripLatency(workerCount);
}

Console.WriteLine();
Console.WriteLine("Karpik.Jobs value scheduler benchmark");
RunValueScheduleComplete();
RunValuePublishRunNext();
RunValueWorkerRuntime();
RunValueParallelBatch();

static int[] BuildWorkerCounts()
{
    Span<int> candidates = stackalloc[] { 1, 2, 4, 8 };
    int maxWorkers = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
    int[] result = new int[candidates.Length];
    int count = 0;

    for (int i = 0; i < candidates.Length; i++)
    {
        int candidate = candidates[i];
        if (candidate <= maxWorkers)
        {
            result[count++] = candidate;
        }
    }

    Array.Resize(ref result, count);
    return result;
}

static void RunIndependent(int workerCount)
{
    JobSystem jobs = new(workerCount, $"JobsBench-independent-{workerCount}");

    try
    {
        WarmupIndependent(jobs);
        ForceFullCollection();

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long beforeTimestamp = Stopwatch.GetTimestamp();

        for (int i = 0; i < IndependentJobs; i++)
        {
            jobs.Enqueue(BenchmarkActions.NoOp);
        }

        jobs.WaitForCompletion();

        long elapsedTicks = Stopwatch.GetTimestamp() - beforeTimestamp;
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        PrintThroughput("independent", "jobs", IndependentJobs, elapsedTicks, allocatedBytes);
    }
    finally
    {
        jobs.Shutdown();
    }
}

static void RunDependencyChain(int workerCount)
{
    JobSystem jobs = new(workerCount, $"JobsBench-chain-{workerCount}");

    try
    {
        WarmupDependencyChain(jobs);
        ForceFullCollection();

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long beforeTimestamp = Stopwatch.GetTimestamp();

        JobHandle previous = jobs.Enqueue(BenchmarkActions.NoOp);
        for (int i = 1; i < ChainJobs; i++)
        {
            previous = jobs.Enqueue(BenchmarkActions.NoOp, previous);
        }

        previous.GetAwaiter().GetResult();
        jobs.WaitForCompletion();

        long elapsedTicks = Stopwatch.GetTimestamp() - beforeTimestamp;
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        PrintThroughput("dependency-chain", "jobs", ChainJobs, elapsedTicks, allocatedBytes);
    }
    finally
    {
        jobs.Shutdown();
    }
}

static void RunParallelBatch(int workerCount)
{
    JobSystem jobs = new(workerCount, $"JobsBench-parallel-{workerCount}");

    try
    {
        WarmupParallel(jobs);
        ForceFullCollection();

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long beforeTimestamp = Stopwatch.GetTimestamp();

        JobHandle handle = jobs.EnqueueParallel(
            BenchmarkActions.NoOpIndex,
            ParallelItems,
            ParallelBatchSize);

        handle.GetAwaiter().GetResult();
        jobs.WaitForCompletion();

        long elapsedTicks = Stopwatch.GetTimestamp() - beforeTimestamp;
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        PrintThroughput("parallel-batch", "items", ParallelItems, elapsedTicks, allocatedBytes);
    }
    finally
    {
        jobs.Shutdown();
    }
}

static void RunRoundTripLatency(int workerCount)
{
    JobSystem jobs = new(workerCount, $"JobsBench-latency-{workerCount}");
    long[] samples = new long[LatencySamples];

    try
    {
        WarmupIndependent(jobs);
        ForceFullCollection();

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < samples.Length; i++)
        {
            long beforeTimestamp = Stopwatch.GetTimestamp();
            JobHandle handle = jobs.Enqueue(BenchmarkActions.NoOp);
            handle.GetAwaiter().GetResult();
            samples[i] = Stopwatch.GetTimestamp() - beforeTimestamp;
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        Array.Sort(samples);

        double p50 = TicksToMicroseconds(samples[PercentileIndex(samples.Length, 50)]);
        double p95 = TicksToMicroseconds(samples[PercentileIndex(samples.Length, 95)]);
        double p99 = TicksToMicroseconds(samples[PercentileIndex(samples.Length, 99)]);

        Console.WriteLine(
            $"  round-trip-latency: samples={LatencySamples}, p50Us={p50:F3}, p95Us={p95:F3}, p99Us={p99:F3}, managedBytes={allocatedBytes}");
    }
    finally
    {
        jobs.Shutdown();
    }
}

static void RunValueScheduleComplete()
{
    using JobScheduler scheduler = new(capacity: 1, maxPayloadByteLength: 64);
    ValueNoOpJob job = default;

    ValueJobHandle warmup = scheduler.Schedule(in job);
    scheduler.Complete(warmup);
    ForceFullCollection();

    long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    long beforeTimestamp = Stopwatch.GetTimestamp();

    for (int i = 0; i < IndependentJobs; i++)
    {
        ValueJobHandle handle = scheduler.Schedule(in job);
        scheduler.Complete(handle);
    }

    long elapsedTicks = Stopwatch.GetTimestamp() - beforeTimestamp;
    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    PrintThroughput("value-schedule-complete", "jobs", IndependentJobs, elapsedTicks, allocatedBytes);
}

static void RunValuePublishRunNext()
{
    using JobScheduler scheduler = new(
        capacity: 1,
        maxPayloadByteLength: 64,
        workerCount: 1,
        workerQueueCapacity: 1);
    ValueNoOpJob job = default;

    ValueJobHandle warmup = scheduler.Schedule(in job);
    scheduler.TryPublish(warmup, workerIndex: 0);
    scheduler.TryRunNext(workerIndex: 0);
    ForceFullCollection();

    long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    long beforeTimestamp = Stopwatch.GetTimestamp();

    for (int i = 0; i < IndependentJobs; i++)
    {
        ValueJobHandle handle = scheduler.Schedule(in job);
        scheduler.TryPublish(handle, workerIndex: 0);
        scheduler.TryRunNext(workerIndex: 0);
    }

    long elapsedTicks = Stopwatch.GetTimestamp() - beforeTimestamp;
    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    PrintThroughput("value-publish-run-next", "jobs", IndependentJobs, elapsedTicks, allocatedBytes);
}

static void RunValueWorkerRuntime()
{
    using JobScheduler scheduler = new(
        capacity: 1,
        maxPayloadByteLength: 64,
        workerCount: 1,
        workerQueueCapacity: 1);
    ValueNoOpJob job = default;

    scheduler.StartWorkers();
    ValueJobHandle warmup = scheduler.Schedule(in job);
    scheduler.TryPublish(warmup, workerIndex: 0);
    WaitUntilCompletedAndReturned(scheduler, warmup);
    ForceFullCollection();

    long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    long beforeTimestamp = Stopwatch.GetTimestamp();

    for (int i = 0; i < LatencySamples; i++)
    {
        ValueJobHandle handle = scheduler.Schedule(in job);
        scheduler.TryPublish(handle, workerIndex: 0);
        WaitUntilCompletedAndReturned(scheduler, handle);
    }

    long elapsedTicks = Stopwatch.GetTimestamp() - beforeTimestamp;
    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    PrintThroughput("value-worker-runtime", "jobs", LatencySamples, elapsedTicks, allocatedBytes);

    scheduler.StopWorkers();
}

static void RunValueParallelBatch()
{
    using JobScheduler scheduler = new(capacity: 1, maxPayloadByteLength: 64);
    ValueNoOpForJob job = default;

    ValueJobHandle warmup = scheduler.ScheduleParallel(in job, ParallelBatchSize, batchSize: ParallelBatchSize);
    scheduler.Complete(warmup);
    ForceFullCollection();

    long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    long beforeTimestamp = Stopwatch.GetTimestamp();

    ValueJobHandle handle = scheduler.ScheduleParallel(in job, ParallelItems, ParallelBatchSize);
    scheduler.Complete(handle);

    long elapsedTicks = Stopwatch.GetTimestamp() - beforeTimestamp;
    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    PrintThroughput("value-parallel-batch", "items", ParallelItems, elapsedTicks, allocatedBytes);
}

static void WaitUntilCompletedAndReturned(JobScheduler scheduler, ValueJobHandle handle)
{
    SpinWait spinWait = default;
    while (!scheduler.IsCompleted(handle) || scheduler.ScheduledCount != 0)
    {
        spinWait.SpinOnce();
    }
}

static void WarmupIndependent(JobSystem jobs)
{
    for (int i = 0; i < WarmupJobs; i++)
    {
        jobs.Enqueue(BenchmarkActions.NoOp);
    }

    jobs.WaitForCompletion();
}

static void WarmupDependencyChain(JobSystem jobs)
{
    JobHandle previous = jobs.Enqueue(BenchmarkActions.NoOp);
    for (int i = 1; i < 128; i++)
    {
        previous = jobs.Enqueue(BenchmarkActions.NoOp, previous);
    }

    previous.GetAwaiter().GetResult();
    jobs.WaitForCompletion();
}

static void WarmupParallel(JobSystem jobs)
{
    JobHandle handle = jobs.EnqueueParallel(
        BenchmarkActions.NoOpIndex,
        ParallelBatchSize * 8,
        ParallelBatchSize);

    handle.GetAwaiter().GetResult();
    jobs.WaitForCompletion();
}

static void ForceFullCollection()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

static void PrintThroughput(
    string name,
    string unitName,
    int units,
    long elapsedTicks,
    long allocatedBytes)
{
    double elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
    double throughputPerSecond = units / (elapsedMs / 1000.0);

    Console.WriteLine(
        $"  {name}: {unitName}={units}, elapsedMs={elapsedMs:F3}, throughputPerSec={throughputPerSecond:F0}, managedBytes={allocatedBytes}");
}

static int PercentileIndex(int length, int percentile)
{
    return Math.Clamp((int)Math.Ceiling(length * percentile / 100.0) - 1, 0, length - 1);
}

static double TicksToMicroseconds(long ticks)
{
    return ticks * 1_000_000.0 / Stopwatch.Frequency;
}

internal static class BenchmarkActions
{
    public static readonly Action NoOp = static () => { };
    public static readonly Action<int> NoOpIndex = static _ => { };
}

internal struct ValueNoOpJob : IJob
{
    public void Execute()
    {
    }
}

internal struct ValueNoOpForJob : IJobFor
{
    public void Execute(int index)
    {
    }
}
