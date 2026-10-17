using System.ComponentModel;
using System.Diagnostics;

namespace Karpik.Engine.Packager;

/// <summary>Сообщает, что дочерний dotnet-процесс не удалось подтверждённо завершить.</summary>
public sealed class DotNetProcessTerminationException : Exception
{
    /// <summary>Создаёт исключение для указанного дочернего процесса.</summary>
    public DotNetProcessTerminationException(int processId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ProcessId = processId;
    }

    /// <summary>Получает идентификатор процесса, требующий ручного восстановления.</summary>
    public int ProcessId { get; }
}

/// <summary>Создаёт управляемую оболочку дочернего dotnet-процесса.</summary>
internal interface IDotNetChildProcessFactory
{
    /// <summary>Создаёт оболочку дочернего процесса для заданной конфигурации запуска.</summary>
    IDotNetChildProcess Create(ProcessStartInfo startInfo);
}

/// <summary>Определяет минимальные операции над дочерним dotnet-процессом.</summary>
internal interface IDotNetChildProcess : IDisposable
{
    /// <summary>Получает идентификатор процесса.</summary>
    int Id { get; }
    /// <summary>Определяет, завершился ли процесс.</summary>
    bool HasExited { get; }
    /// <summary>Получает код завершения процесса.</summary>
    int ExitCode { get; }
    /// <summary>Запускает процесс.</summary>
    void Start();
    /// <summary>Асинхронно читает stdout до конца.</summary>
    Task<string> ReadStandardOutputToEndAsync();
    /// <summary>Асинхронно читает stderr до конца.</summary>
    Task<string> ReadStandardErrorToEndAsync();
    /// <summary>Ожидает завершения процесса.</summary>
    Task WaitForExitAsync(CancellationToken cancellationToken);
    /// <summary>Завершает процесс или всё его дерево.</summary>
    void Kill(bool entireProcessTree);
}

/// <summary>Запускает dotnet с таймаутом и подтверждённым завершением дерева процессов.</summary>
internal sealed class DotNetProcessRunner
{
    private static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultTerminationTimeout = TimeSpan.FromSeconds(15);
    private readonly IDotNetChildProcessFactory _processFactory;
    private readonly TimeSpan _executionTimeout;
    private readonly TimeSpan _terminationTimeout;

    /// <summary>Создаёт runner со стандартными таймаутами и process factory.</summary>
    public DotNetProcessRunner()
        : this(new SystemDotNetChildProcessFactory(), DefaultExecutionTimeout, DefaultTerminationTimeout)
    {
    }

    /// <summary>Создаёт runner с зависимостями и таймаутами для тестирования.</summary>
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

    /// <summary>Запускает dotnet с аргументами и выбрасывает ошибку при неуспехе или таймауте.</summary>
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

    /// <summary>Завершает процесс и подтверждает его остановку до возврата вызывающему коду.</summary>
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

    /// <summary>Безопасно проверяет завершение процесса.</summary>
    private static bool TryHasExited(IDotNetChildProcess process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    /// <summary>Создаёт исключение для неподтверждённого завершения процесса.</summary>
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

/// <summary>Создаёт оболочки над System.Diagnostics.Process.</summary>
internal sealed class SystemDotNetChildProcessFactory : IDotNetChildProcessFactory
{
    /// <summary>Создаёт оболочку для ещё не запущенного процесса.</summary>
    public IDotNetChildProcess Create(ProcessStartInfo startInfo) => new SystemDotNetChildProcess(startInfo);
}

/// <summary>Адаптирует System.Diagnostics.Process к внутреннему контракту runner'а.</summary>
internal sealed class SystemDotNetChildProcess : IDotNetChildProcess
{
    private readonly Process _process;

    /// <summary>Создаёт ещё не запущенный процесс из его start info.</summary>
    public SystemDotNetChildProcess(ProcessStartInfo startInfo) =>
        _process = new Process { StartInfo = startInfo };

    /// <summary>Получает идентификатор процесса.</summary>
    public int Id => _process.Id;
    /// <summary>Определяет, завершился ли процесс.</summary>
    public bool HasExited => _process.HasExited;
    /// <summary>Получает код завершения процесса.</summary>
    public int ExitCode => _process.ExitCode;

    /// <summary>Запускает процесс.</summary>
    public void Start()
    {
        if (!_process.Start())
        {
            throw new InvalidOperationException("Failed to start the dotnet child process.");
        }
    }

    /// <summary>Асинхронно читает stdout до конца.</summary>
    public Task<string> ReadStandardOutputToEndAsync() => _process.StandardOutput.ReadToEndAsync();
    /// <summary>Асинхронно читает stderr до конца.</summary>
    public Task<string> ReadStandardErrorToEndAsync() => _process.StandardError.ReadToEndAsync();
    /// <summary>Ожидает завершения процесса.</summary>
    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);
    /// <summary>Завершает процесс или всё его дерево.</summary>
    public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);
    /// <summary>Освобождает ресурсы процесса.</summary>
    public void Dispose() => _process.Dispose();
}
