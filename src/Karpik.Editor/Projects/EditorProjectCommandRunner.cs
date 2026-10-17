using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Karpik.Editor;

public sealed class EditorProjectCommandRunner : IAsyncDisposable
{
    private const int MaximumOutputCharacters = 128 * 1024;
    private static readonly TimeSpan DefaultTerminationTimeout = TimeSpan.FromSeconds(15);
    private readonly IMsBuildProcessFactory _processFactory;
    private readonly TimeSpan _terminationTimeout;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _disposeGate = new(1, 1);
    private readonly object _stateGate = new();
    private ActiveCommand? _active;
    private int _shutdownRequested;
    private int _disposed;

    public EditorProjectCommandRunner(
        IMsBuildProcessFactory? processFactory = null,
        TimeSpan? terminationTimeout = null)
    {
        _processFactory = processFactory ?? new SystemMsBuildProcessFactory();
        _terminationTimeout = terminationTimeout ?? DefaultTerminationTimeout;
        if (_terminationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(terminationTimeout));
        }
    }

    public async Task RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        Action<string> output,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ThrowIfUnavailable();
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = Path.GetFullPath(workingDirectory),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            IMsBuildProcess process = _processFactory.Start(startInfo);
            var active = new ActiveCommand(
                process,
                process.ReadStandardOutputAsync(MaximumOutputCharacters, CancellationToken.None),
                process.ReadStandardErrorAsync(MaximumOutputCharacters, CancellationToken.None),
                output);
            lock (_stateGate)
            {
                _active = active;
            }

            CommandCompletion completion;
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                Volatile.Write(ref active.ExitConfirmed, 1);
                completion = await CompleteAsync(active);
            }
            catch (OperationCanceledException)
            {
                Volatile.Write(ref active.CancellationRequested, 1);
                await TerminateAndConfirmAsync(active);
                await CompleteAsync(active);
                throw;
            }

            if (Volatile.Read(ref active.CancellationRequested) != 0)
            {
                throw new OperationCanceledException("The active project command was cancelled.");
            }
            ThrowCompletionFailure(completion);
            if (completion.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"dotnet {string.Join(' ', arguments)} failed with exit code {completion.ExitCode}.");
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task CancelActiveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ActiveCommand? active;
        lock (_stateGate)
        {
            active = _active;
        }
        if (active is null)
        {
            return;
        }

        Volatile.Write(ref active.CancellationRequested, 1);
        await TerminateAndConfirmAsync(active);
        CommandCompletion completion = await CompleteAsync(active);
        if (completion.DisposalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(completion.DisposalFailure).Throw();
        }
    }

    private async Task TerminateAndConfirmAsync(ActiveCommand active)
    {
        await active.TerminationGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref active.ExitConfirmed) != 0)
            {
                return;
            }
            try
            {
                active.Process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                               or IOException
                                               or NotSupportedException
                                               or Win32Exception)
            {
                // The bounded exit wait below is the source of truth.
            }

            using var confirmation = new CancellationTokenSource(_terminationTimeout);
            try
            {
                await active.Process.WaitForExitAsync(confirmation.Token);
            }
            catch (OperationCanceledException) when (confirmation.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The active dotnet process did not confirm exit within {_terminationTimeout}.");
            }
            Volatile.Write(ref active.ExitConfirmed, 1);
        }
        finally
        {
            active.TerminationGate.Release();
        }
    }

    private async Task<CommandCompletion> CompleteAsync(ActiveCommand active)
    {
        await active.CompletionGate.WaitAsync();
        try
        {
            if (active.Completion is not null)
            {
                return active.Completion;
            }

            int exitCode = -1;
            Exception? outputFailure = null;
            Exception? disposalFailure = null;
            try
            {
                string standardOutput = await active.StandardOutput;
                string standardError = await active.StandardError;
                EmitLines(standardOutput, active.Output);
                EmitLines(standardError, active.Output);
                exitCode = active.Process.ExitCode;
            }
            catch (Exception exception)
            {
                outputFailure = exception;
            }

            try
            {
                await active.Process.DisposeAsync();
            }
            catch (Exception exception)
            {
                disposalFailure = exception;
            }
            finally
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_active, active))
                    {
                        _active = null;
                    }
                }
                active.Completion = new CommandCompletion(
                    exitCode,
                    outputFailure,
                    disposalFailure);
            }
            return active.Completion;
        }
        finally
        {
            active.CompletionGate.Release();
        }
    }

    private static void EmitLines(string text, Action<string> output)
    {
        foreach (string line in text.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            output(line);
        }
    }

    private static void ThrowCompletionFailure(CommandCompletion completion)
    {
        Exception? failure = completion.DisposalFailure ?? completion.OutputFailure;
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }
            Volatile.Write(ref _shutdownRequested, 1);
            await CancelActiveAsync(CancellationToken.None);
            await _commandGate.WaitAsync();
            try
            {
                Volatile.Write(ref _disposed, 1);
            }
            finally
            {
                _commandGate.Release();
            }
        }
        finally
        {
            _disposeGate.Release();
        }
    }

    private void ThrowIfUnavailable() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _shutdownRequested) != 0 || Volatile.Read(ref _disposed) != 0,
            this);

    private sealed class ActiveCommand(
        IMsBuildProcess process,
        Task<string> standardOutput,
        Task<string> standardError,
        Action<string> output)
    {
        public IMsBuildProcess Process { get; } = process;
        public Task<string> StandardOutput { get; } = standardOutput;
        public Task<string> StandardError { get; } = standardError;
        public Action<string> Output { get; } = output;
        public SemaphoreSlim TerminationGate { get; } = new(1, 1);
        public SemaphoreSlim CompletionGate { get; } = new(1, 1);
        public CommandCompletion? Completion { get; set; }
        public int CancellationRequested;
        public int ExitConfirmed;
    }

    private sealed record CommandCompletion(
        int ExitCode,
        Exception? OutputFailure,
        Exception? DisposalFailure);
}
