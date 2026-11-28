using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Collections.Concurrent;

namespace Karpik.Engine.Core;

internal interface IClientSimulationLoop
{
    void RunGameplayFrame(double deltaTime);
    bool IsApplicationRunning { get; }
}

internal sealed class ClientSimulationWorker : IDisposable
{
    private readonly IClientSimulationLoop _loop;
    private readonly ClientFrameMetrics? _metrics;
    private readonly AutoResetEvent _workAvailable = new(false);
    private readonly ConcurrentQueue<Action> _pendingWork = new();
    private readonly Thread _thread;
    private int _frameReserved;
    private int _frameReady;
    private int _stopRequested;
    private int _simulationRunning = 1;
    private long _requestedDeltaBits;
    private long _requestedAtTimestamp;
    private Exception? _workerException;
    // Only maintenance and shutdown take this gate; frame submission stays lock-free.
    private readonly object _maintenanceGate = new();
    private int _maintenanceActive;
    private int _maintenanceOwnerThreadId;
    private bool _disposed;

    public ClientSimulationWorker(IClientSimulationLoop loop, ClientFrameMetrics? metrics = null)
    {
        _loop = loop;
        _metrics = metrics;
        _thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "Karpik Client Simulation"
        };
        _thread.Start();
    }

    public bool IsSimulationRunning => Volatile.Read(ref _simulationRunning) != 0;

    internal bool OwnsMaintenanceOnCurrentThread => Volatile.Read(ref _maintenanceActive) != 0 &&
        _maintenanceOwnerThreadId == Environment.CurrentManagedThreadId;

    internal bool TryEnterMaintenance(out MaintenanceLease? lease)
    {
        lease = null;
        lock (_maintenanceGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopRequested) != 0, this);
            ThrowIfFaulted();
            if (Interlocked.CompareExchange(ref _frameReserved, 1, 0) != 0)
                return false;

            try
            {
                lease = new MaintenanceLease(this);
                _maintenanceOwnerThreadId = Environment.CurrentManagedThreadId;
                Volatile.Write(ref _maintenanceActive, 1);
                return true;
            }
            catch
            {
                Volatile.Write(ref _frameReserved, 0);
                _workAvailable.Set();
                throw;
            }
        }
    }

    /// <summary>
    /// Holds the worker idle, including queued control work, until released.
    /// Release this lease before disposing its worker on the acquiring thread.
    /// </summary>
    internal sealed class MaintenanceLease : IDisposable
    {
        private ClientSimulationWorker? _owner;

        internal MaintenanceLease(ClientSimulationWorker owner) => _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitMaintenance();
    }

    private void ExitMaintenance()
    {
        lock (_maintenanceGate)
        {
            _maintenanceOwnerThreadId = 0;
            Volatile.Write(ref _maintenanceActive, 0);
            Volatile.Write(ref _frameReserved, 0);
            // Wake both queued control work and a shutdown waiting for the lease.
            _workAvailable.Set();
        }
    }

    public bool TryReserveFrame()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopRequested) != 0, this);
        ThrowIfFaulted();
        return Interlocked.CompareExchange(ref _frameReserved, 1, 0) == 0;
    }

    public void StartReservedFrame(double deltaTime)
    {
        if (Volatile.Read(ref _maintenanceActive) != 0)
            throw new InvalidOperationException("A maintenance reservation cannot start a simulation frame.");
        if (Volatile.Read(ref _frameReserved) == 0)
        {
            throw new InvalidOperationException("A simulation frame must be reserved before it is started.");
        }

        Volatile.Write(ref _requestedDeltaBits, BitConverter.DoubleToInt64Bits(deltaTime));
        Volatile.Write(ref _requestedAtTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref _frameReady, 1);
        _workAvailable.Set();
    }

    public void CancelReservedFrame()
    {
        if (Volatile.Read(ref _maintenanceActive) != 0)
            throw new InvalidOperationException("A maintenance reservation must be released through its lease.");
        Volatile.Write(ref _frameReady, 0);
        Volatile.Write(ref _frameReserved, 0);
        if (!_pendingWork.IsEmpty)
        {
            _workAvailable.Set();
        }
    }

    public void ThrowIfFaulted()
    {
        Exception? exception = Volatile.Read(ref _workerException);
        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopRequested) != 0, this);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingWork.Enqueue(() =>
        {
            try
            {
                completion.TrySetResult(work());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        _workAvailable.Set();
        return completion.Task;
    }

    public void Dispose()
    {
        if (Thread.CurrentThread == _thread)
            throw new InvalidOperationException("A simulation worker cannot join its own thread.");

        lock (_maintenanceGate)
        {
            if (_disposed) return;
            if (_maintenanceActive != 0 && _maintenanceOwnerThreadId == Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Release the maintenance lease before disposing its worker.");
            Volatile.Write(ref _stopRequested, 1);
            _workAvailable.Set();
        }

        _thread.Join();
        lock (_maintenanceGate)
        {
            if (_disposed) return;
            _disposed = true;
            _workAvailable.Dispose();
        }
        ThrowIfFaulted();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _workAvailable.WaitOne();
            if (Volatile.Read(ref _stopRequested) != 0)
            {
                // A different thread may request shutdown during maintenance.
                // Do not execute queued work or release worker resources until it ends.
                if (Volatile.Read(ref _maintenanceActive) != 0)
                    continue;
                ExecutePendingWork();
                return;
            }

            if (Interlocked.Exchange(ref _frameReady, 0) == 0)
            {
                if (Interlocked.CompareExchange(ref _frameReserved, 1, 0) != 0)
                {
                    continue;
                }

                ExecutePendingWork();
                Volatile.Write(ref _frameReserved, 0);
                continue;
            }

            try
            {
                long startedAt = Stopwatch.GetTimestamp();
                double deltaTime = BitConverter.Int64BitsToDouble(Volatile.Read(ref _requestedDeltaBits));
                _loop.RunGameplayFrame(deltaTime);
                _metrics?.PublishSimulation(startedAt - Volatile.Read(ref _requestedAtTimestamp), Stopwatch.GetTimestamp() - startedAt);
                Volatile.Write(ref _simulationRunning, _loop.IsApplicationRunning ? 1 : 0);
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _workerException, exception);
            }
            finally
            {
                ExecutePendingWork();
                Volatile.Write(ref _frameReserved, 0);
            }
        }
    }

    private void ExecutePendingWork()
    {
        while (_pendingWork.TryDequeue(out Action? work))
        {
            work();
        }
    }
}
