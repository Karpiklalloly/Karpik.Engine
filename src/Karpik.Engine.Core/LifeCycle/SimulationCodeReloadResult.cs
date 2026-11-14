namespace Karpik.Engine.Core;

public enum SimulationCodeReloadStatus
{
    Success,
    Busy,
    Unsupported,
    InvalidState,
    Cancelled,
    Rejected,
    Faulted
}

/// <summary>Local host result. This is not a network or persistence contract.</summary>
public readonly record struct SimulationCodeReloadResult(SimulationCodeReloadStatus Status, Exception? Error = null);

/// <summary>Cleanup could not prove host integrity; the Simulation must not resume.</summary>
public sealed class SimulationCodeReloadCleanupException(string message, Exception innerException)
    : Exception(message, innerException);

/// <summary>
/// Optional host boundary that excludes frame submissions and background Simulation work.
/// Enter runs on the main thread. The returned lease is released on the main thread after cleanup.
/// </summary>
public interface ISimulationCodeReloadBoundary
{
    ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken);
}
