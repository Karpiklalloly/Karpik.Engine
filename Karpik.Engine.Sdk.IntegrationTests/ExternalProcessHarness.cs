using System.Diagnostics;

namespace Karpik.Engine.Sdk.IntegrationTests;

internal interface IExternalChildProcess
{
    int Id { get; }
    bool HasExited { get; }
    void Kill(bool entireProcessTree);
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal sealed class SystemExternalChildProcess(Process process) : IExternalChildProcess
{
    public int Id => process.Id;
    public bool HasExited => process.HasExited;
    public void Kill(bool entireProcessTree) => process.Kill(entireProcessTree);
    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
}

internal sealed class ExternalProcessTerminationException : Exception
{
    public ExternalProcessTerminationException(int processId, string message, Exception innerException)
        : base(message, innerException) => ProcessId = processId;

    public int ProcessId { get; }
}

internal static class ExternalTemporaryState
{
    public static async Task RunAsync(
        string root,
        Func<Task> action,
        Action<string> deleteOwnedRoot)
    {
        bool cleanupAllowed = true;
        try
        {
            await action();
        }
        catch (ExternalProcessTerminationException)
        {
            cleanupAllowed = false;
            throw;
        }
        finally
        {
            if (cleanupAllowed)
            {
                deleteOwnedRoot(root);
            }
        }
    }
}

internal static class ExternalProcessTermination
{
    public static async Task EnsureStoppedAsync(
        IExternalChildProcess process,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Exception? livenessFailure = null;
        if (TryHasExited(process, out Exception? initialFailure))
        {
            return;
        }
        livenessFailure = initialFailure;

        try
        {
            Task kill = Task.Run(() => process.Kill(entireProcessTree: true));
            if (await Task.WhenAny(kill, Task.Delay(timeout)) != kill)
            {
                throw PreservationFailure(
                    process.Id,
                    $"Process-tree kill did not complete within {timeout}.",
                    new TimeoutException("Process-tree kill did not complete."));
            }
            await kill;
        }
        catch (ExternalProcessTerminationException)
        {
            throw;
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            livenessFailure = exception;
        }

        using var waitCancellation = new CancellationTokenSource(timeout);
        Task wait;
        try
        {
            wait = process.WaitForExitAsync(waitCancellation.Token);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            throw PreservationFailure(process.Id, "Waiting for process exit failed.", exception);
        }

        if (await Task.WhenAny(wait, Task.Delay(timeout)) != wait)
        {
            throw PreservationFailure(
                process.Id,
                $"Process exit was not confirmed within {timeout} after termination.",
                livenessFailure ?? new TimeoutException("Process exit was not confirmed."));
        }
        try
        {
            await wait;
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            throw PreservationFailure(process.Id, "Waiting for process exit failed.", exception);
        }

        if (!TryHasExited(process, out Exception? finalFailure))
        {
            throw PreservationFailure(
                process.Id,
                "Process wait completed without confirmed exit.",
                finalFailure ?? livenessFailure ?? new InvalidOperationException("Process liveness is unconfirmed."));
        }
    }

    public static ExternalProcessTerminationException PreservationFailure(
        int processId,
        string reason,
        Exception innerException) =>
        new(
            processId,
            $"Could not confirm termination of dotnet process {processId}: {reason} Temporary integration state was preserved.",
            innerException);

    public static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException;

    private static bool TryHasExited(IExternalChildProcess process, out Exception? failure)
    {
        try
        {
            failure = null;
            return process.HasExited;
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            failure = exception;
            return false;
        }
    }
}
