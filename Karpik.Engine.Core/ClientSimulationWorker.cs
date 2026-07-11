using System.Diagnostics;
using System.Runtime.ExceptionServices;

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
    private readonly Thread _thread;
    private int _frameReserved;
    private int _stopRequested;
    private int _simulationRunning = 1;
    private long _requestedDeltaBits;
    private long _requestedAtTimestamp;
    private Exception? _workerException;

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

    public bool TryReserveFrame()
    {
        ThrowIfFaulted();
        return Interlocked.CompareExchange(ref _frameReserved, 1, 0) == 0;
    }

    public void StartReservedFrame(double deltaTime)
    {
        if (Volatile.Read(ref _frameReserved) == 0)
        {
            throw new InvalidOperationException("A simulation frame must be reserved before it is started.");
        }

        Volatile.Write(ref _requestedDeltaBits, BitConverter.DoubleToInt64Bits(deltaTime));
        Volatile.Write(ref _requestedAtTimestamp, Stopwatch.GetTimestamp());
        _workAvailable.Set();
    }

    public void CancelReservedFrame()
    {
        Volatile.Write(ref _frameReserved, 0);
    }

    public void ThrowIfFaulted()
    {
        Exception? exception = Volatile.Read(ref _workerException);
        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    public void Dispose()
    {
        Volatile.Write(ref _stopRequested, 1);
        _workAvailable.Set();
        _thread.Join();
        _workAvailable.Dispose();
        ThrowIfFaulted();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _workAvailable.WaitOne();
            if (Volatile.Read(ref _stopRequested) != 0)
            {
                return;
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
                Volatile.Write(ref _frameReserved, 0);
            }
        }
    }
}
