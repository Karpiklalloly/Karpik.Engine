using System.ComponentModel;
using System.Diagnostics;

namespace Karpik.Engine.Packager;

public sealed class DotNetProcessTerminationException : Exception
{
    public DotNetProcessTerminationException(int processId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ProcessId = processId;
    }

    public int ProcessId { get; }
}

internal interface IDotNetChildProcessFactory
{
    IDotNetChildProcess Create(ProcessStartInfo startInfo);
}

internal interface IDotNetChildProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    void Start();
    Task<string> ReadStandardOutputToEndAsync();
    Task<string> ReadStandardErrorToEndAsync();
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void Kill(bool entireProcessTree);
}

internal sealed class DotNetProcessRunner
{
    private static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultTerminationTimeout = TimeSpan.FromSeconds(15);
    private readonly IDotNetChildProcessFactory _processFactory;
    private readonly TimeSpan _executionTimeout;
    private readonly TimeSpan _terminationTimeout;

    public DotNetProcessRunner()
        : this(new SystemDotNetChildProcessFactory(), DefaultExecutionTimeout, DefaultTerminationTimeout)
    {
    }

    internal DotNetProcessRunner(
        IDotNetChildProcessFactory processFactory,
        TimeSpan executionTimeout,
        TimeSpan terminationTimeout)
    {
        ArgumentNullException.ThrowIfNull(processFactory);
        if (executionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(executionTimeout));
        if (terminationTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(terminationTimeout));
        _processFactory = processFactory;
        _executionTimeout = executionTimeout;
        _terminationTimeout = terminationTimeout;
    }

    public void Run(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using IDotNetChildProcess process = _processFactory.Create(startInfo);
        process.Start();
        int processId = process.Id;
        Task<string> standardOutput = process.ReadStandardOutputToEndAsync();
        Task<string> standardError = process.ReadStandardErrorToEndAsync();
        using var executionTimeout = new CancellationTokenSource(_executionTimeout);
        try
        {
            process.WaitForExitAsync(executionTimeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (executionTimeout.IsCancellationRequested)
        {
            TerminateAndConfirm(process, processId, arguments);
            string timedOutOutput = standardOutput.GetAwaiter().GetResult();
            string timedOutError = standardError.GetAwaiter().GetResult();
            throw new TimeoutException(
                $"dotnet {string.Join(' ', arguments)} exceeded the {_executionTimeout} packaging timeout and was terminated.{Environment.NewLine}{timedOutOutput}{Environment.NewLine}{timedOutError}");
        }

        string output = standardOutput.GetAwaiter().GetResult();
        string error = standardError.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }

    private void TerminateAndConfirm(IDotNetChildProcess process, int processId, IReadOnlyList<string> arguments)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            if (!TryHasExited(process))
            {
                throw CreateTerminationException(processId, arguments, "process-tree kill failed", exception);
            }
        }

        using var terminationTimeout = new CancellationTokenSource(_terminationTimeout);
        try
        {
            process.WaitForExitAsync(terminationTimeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (terminationTimeout.IsCancellationRequested)
        {
            throw CreateTerminationException(processId, arguments, $"exit was not confirmed within {_terminationTimeout}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            throw CreateTerminationException(processId, arguments, "waiting for process exit failed", exception);
        }

        if (!TryHasExited(process))
        {
            throw CreateTerminationException(processId, arguments, "the process wait completed without a confirmed exit");
        }
    }

    private static bool TryHasExited(IDotNetChildProcess process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    private static DotNetProcessTerminationException CreateTerminationException(
        int processId,
        IReadOnlyList<string> arguments,
        string reason,
        Exception? innerException = null) =>
        new(
            processId,
            $"Could not confirm termination of dotnet process {processId} after '{string.Join(' ', arguments)}': {reason}. The owned transaction workspace was preserved.",
            innerException);
}

internal sealed class SystemDotNetChildProcessFactory : IDotNetChildProcessFactory
{
    public IDotNetChildProcess Create(ProcessStartInfo startInfo) => new SystemDotNetChildProcess(startInfo);
}

internal sealed class SystemDotNetChildProcess : IDotNetChildProcess
{
    private readonly Process _process;

    public SystemDotNetChildProcess(ProcessStartInfo startInfo) =>
        _process = new Process { StartInfo = startInfo };

    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;

    public void Start()
    {
        if (!_process.Start())
        {
            throw new InvalidOperationException("Failed to start the dotnet child process.");
        }
    }

    public Task<string> ReadStandardOutputToEndAsync() => _process.StandardOutput.ReadToEndAsync();
    public Task<string> ReadStandardErrorToEndAsync() => _process.StandardError.ReadToEndAsync();
    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);
    public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);
    public void Dispose() => _process.Dispose();
}
