using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Karpik.Jobs;

public class JobSystem
{
    private const int CacheLineSize = 64;
    private const int MaxThreads = 64;
    private const int DefaultQueueCapacity = 100_000;
    private const int DefaultBatchSize = 64;

    private readonly ThreadState[] _threadStates;
    private readonly Thread[] _threads;
    private readonly int _workerCount;
    private volatile bool _isRunning;
    private readonly ObjectPool<JobWrapper> _jobWrapperPool;

    private int _enqueueIndex = 0;
    private volatile int _outstandingJobs = 0;

    private readonly SemaphoreSlim _workSemaphore = new SemaphoreSlim(0);

    public JobSystem(int workerCount = -1)
    {
        _workerCount = workerCount == -1
            ? Math.Min(Environment.ProcessorCount, MaxThreads)
            : Math.Min(workerCount, MaxThreads);

        _threadStates = new ThreadState[_workerCount];
        _threads = new Thread[_workerCount];
        _jobWrapperPool = new ObjectPool<JobWrapper>(() => new JobWrapper(), 100_000);

        for (int i = 0; i < _workerCount; i++)
        {
            _threadStates[i] = new ThreadState(i);
            _threads[i] = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = $"JobWorker-{i}"
            };
            _threads[i].Start(i);
        }

        _isRunning = true;
    }

    [StructLayout(LayoutKind.Explicit, Size = CacheLineSize * 2)]
    private class ThreadState
    {
        [FieldOffset(CacheLineSize)]
        public readonly int ThreadId;
        [FieldOffset(CacheLineSize * 2 - 16)]
        public readonly ConcurrentQueue<JobWrapper> Queue;
        [FieldOffset(CacheLineSize * 2 - 8)]
        public volatile bool IsWorking;

        public ThreadState(int threadId)
        {
            ThreadId = threadId;
            Queue = new ConcurrentQueue<JobWrapper>();
        }
    }

    private void WorkerLoop(object state)
    {
        int threadId = (int)state;

        while (true)
        {
            _workSemaphore.Wait();

            if (!_isRunning)
            {
                break;
            }

            while (true)
            {
                if (TryPopTask(threadId, out var wrapper) || TryStealTask(threadId, out wrapper))
                {
                    ExecuteJob(wrapper, threadId);
                    break;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryPopTask(int threadId, out JobWrapper wrapper)
    {
        return _threadStates[threadId].Queue.TryDequeue(out wrapper);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryStealTask(int thiefId, out JobWrapper wrapper)
    {
        int victimId = (thiefId + 1 + ThreadLocalRandom.Next(0, _workerCount - 1)) % _workerCount;
        if (victimId == thiefId)
        {
            victimId = (thiefId + 1) % _workerCount;
        }

        return _threadStates[victimId].Queue.TryDequeue(out wrapper);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteJob(JobWrapper wrapper, int threadId)
    {
        var state = _threadStates[threadId];
        state.IsWorking = true;

        try
        {
            if (!wrapper.Cts.Token.IsCancellationRequested)
            {
                if (wrapper.IsParallel)
                    wrapper.ParallelAction(wrapper.StartIndex, wrapper.EndIndex);
                else
                    wrapper.Action();
            }
        }
        catch (Exception ex)
        {
            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine($"[ERROR] Job failed: {ex.Message}");
            Console.ForegroundColor = color;
        }
        finally
        {
            state.IsWorking = false;

            wrapper.OnCompleted?.Invoke();
            wrapper.Completion?.Signal();
            _jobWrapperPool.Return(wrapper);

            Interlocked.Decrement(ref _outstandingJobs);
        }
    }

    private JobHandle EnqueueInternal(Action job, JobHandle[] dependencies)
    {
        if (!_isRunning) return default;

        var completion = new JobCompletion(1);
        var cts = new CancellationTokenSource();
        var wrapper = _jobWrapperPool.Rent();
        wrapper.Action = job;
        wrapper.Cts = cts;
        wrapper.IsParallel = false;
        wrapper.Completion = completion;

        Action enqueueAction = () =>
        {
            Interlocked.Increment(ref _outstandingJobs);
            int targetThread = (Interlocked.Increment(ref _enqueueIndex) & int.MaxValue) % _workerCount;
            _threadStates[targetThread].Queue.Enqueue(wrapper);

            _workSemaphore.Release();
        };

        if (dependencies == null || dependencies.Length == 0)
        {
            enqueueAction();
        }
        else
        {
            var dependenciesCompletion = new JobCompletion(dependencies.Length);
            foreach (var dependency in dependencies)
            {
                dependency.Completion.AddContinuation(() => dependenciesCompletion.Signal());
            }

            dependenciesCompletion.AddContinuation(enqueueAction);
        }

        return new JobHandle(completion, cts);
    }

    private JobHandle EnqueueParallelInternal(Action<int> action, int size, int batchSize, JobHandle[] dependencies)
    {
        if (!_isRunning || size <= 0) return default;
        if (batchSize <= 0) batchSize = Math.Max(1, Math.Min(DefaultBatchSize, size / _workerCount));
        int batchCount = (size + batchSize - 1) / batchSize;
        var cts = new CancellationTokenSource();
        var completion = new JobCompletion(batchCount);

        Action enqueueBatches = () =>
        {
            Interlocked.Add(ref _outstandingJobs, batchCount);

            for (int i = 0; i < batchCount; i++)
            {
                int startIndex = i * batchSize;
                int endIndex = Math.Min(startIndex + batchSize, size);
                var wrapper = _jobWrapperPool.Rent();
                wrapper.ParallelAction = (start, end) =>
                {
                    for (int j = start; j < end; j++)
                    {
                        if (wrapper.Cts.Token.IsCancellationRequested) return;
                        action(j);
                    }
                };
                wrapper.Cts = cts;
                wrapper.IsParallel = true;
                wrapper.StartIndex = startIndex;
                wrapper.EndIndex = endIndex;
                wrapper.Completion = completion;

                int targetThread = (Interlocked.Increment(ref _enqueueIndex) & int.MaxValue) % _workerCount;
                _threadStates[targetThread].Queue.Enqueue(wrapper);
            }

            _workSemaphore.Release(batchCount);
        };

        if (dependencies == null || dependencies.Length == 0)
        {
            enqueueBatches();
        }
        else
        {
            var dependenciesCompletion = new JobCompletion(dependencies.Length);
            foreach (var dependency in dependencies)
            {
                dependency.Completion.AddContinuation(() => dependenciesCompletion.Signal());
            }

            dependenciesCompletion.AddContinuation(enqueueBatches);
        }

        return new JobHandle(completion, cts);
    }

    public JobHandle Enqueue(Action job) =>
        EnqueueInternal(job, null);

    public JobHandle Enqueue(Action job, params JobHandle[] dependencies) =>
        EnqueueInternal(job, dependencies);

    public JobHandle EnqueueParallel(Action<int> action, int size, int batchSize = -1) =>
        EnqueueParallelInternal(action, size, batchSize, null);

    public JobHandle EnqueueParallel(Action<int> action, int size, int batchSize = -1,
        params JobHandle[] dependencies) =>
        EnqueueParallelInternal(action, size, batchSize, dependencies);

    public static JobHandle Combine(params JobHandle[] handles)
    {
        if (handles == null || handles.Length == 0) return default;
        var completion = new JobCompletion(handles.Length);
        foreach (var handle in handles)
        {
            handle.Completion.AddContinuation(() => completion.Signal());
        }

        return new JobHandle(completion, null);
    }

    public void WaitForCompletion()
    {
        var spinWait = new SpinWait();
        while (Volatile.Read(ref _outstandingJobs) > 0)
        {
            spinWait.SpinOnce();
        }
    }

    public void Shutdown()
    {
        _isRunning = false;
        _jobWrapperPool.Dispose();

        _workSemaphore.Release(_workerCount);
        foreach (var thread in _threads)
        {
            thread.Join();
        }

        Array.Clear(_threadStates, 0, _threadStates.Length);
        Array.Clear(_threads, 0, _threads.Length);
    }

    public void Dispose() => Shutdown();
}