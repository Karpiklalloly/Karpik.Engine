using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Karpik.Engine.ProjectModel;
using Microsoft.Win32.SafeHandles;

namespace Karpik.Editor;

public sealed record MsBuildProjectEvaluation(
    string ProjectPath,
    string Kind,
    string Side,
    string RuntimeBundlePath,
    string EngineRoot,
    string TargetPath,
    IReadOnlyList<string> ProjectReferences,
    string CompositionMode);

public interface IMsBuildProjectInspector
{
    Task<IReadOnlyList<MsBuildProjectEvaluation>> InspectAsync(
        KarpikSolutionModel solution,
        string engineRoot,
        CancellationToken cancellationToken,
        ProjectInputLease? inputLease = null);

    Task<IReadOnlyList<MsBuildProjectEvaluation>> InspectAsync(
        KarpikSolutionModel solution,
        string engineRoot,
        CancellationToken cancellationToken,
        BuildConfiguration configuration,
        ProjectInputLease? inputLease = null) =>
        InspectAsync(solution, engineRoot, cancellationToken, inputLease);
}

public interface IMsBuildProcessFactory
{
    IMsBuildProcess Start(ProcessStartInfo startInfo);
}

public interface IMsBuildProcess : IAsyncDisposable
{
    int ExitCode { get; }
    Task<string> ReadStandardOutputAsync(int maximumCharacters, CancellationToken cancellationToken);
    Task<string> ReadStandardErrorAsync(int maximumCharacters, CancellationToken cancellationToken);
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void Kill(bool entireProcessTree);
}

public sealed class MsBuildProjectInspector : IMsBuildProjectInspector
{
    private const int MaximumOutputCharacters = 128 * 1024;
    private const int MaximumResultBytes = 1024 * 1024;
    private const string ResultArgumentPrefix = "-getResultOutputFile:";
    private static readonly string PropertyArgument =
        "-getProperty:MSBuildProjectFullPath,KarpikProjectKind,KarpikSide,KarpikCompositionMode,KarpikRuntimeBundlePath,KarpikEngineRoot,TargetPath";

    private readonly IMsBuildProcessFactory _processFactory;
    private readonly TimeSpan _evaluationTimeout;
    private readonly TimeSpan _terminationTimeout;
    private readonly string _dotNetExecutable;
    private readonly RetainedProcessReaper _reaper;

    public MsBuildProjectInspector(
        TimeSpan? evaluationTimeout = null,
        TimeSpan? terminationTimeout = null,
        string dotNetExecutable = "dotnet")
        : this(
            new SystemMsBuildProcessFactory(),
            evaluationTimeout ?? Timeout.InfiniteTimeSpan,
            terminationTimeout ?? TimeSpan.FromSeconds(5),
            dotNetExecutable,
            maximumRetainedProcesses: 8)
    {
    }

    public MsBuildProjectInspector(
        IMsBuildProcessFactory processFactory,
        TimeSpan evaluationTimeout,
        TimeSpan terminationTimeout,
        string dotNetExecutable = "dotnet",
        int maximumRetainedProcesses = 8)
    {
        ArgumentNullException.ThrowIfNull(processFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dotNetExecutable);
        if (evaluationTimeout != Timeout.InfiniteTimeSpan && evaluationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(evaluationTimeout));
        }
        if (terminationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(terminationTimeout));
        }
        if (maximumRetainedProcesses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedProcesses));
        }

        _processFactory = processFactory;
        _evaluationTimeout = evaluationTimeout;
        _terminationTimeout = terminationTimeout;
        _dotNetExecutable = dotNetExecutable;
        _reaper = new RetainedProcessReaper(
            maximumRetainedProcesses,
            terminationTimeout);
    }

    public int RetainedProcessCount => _reaper.ActiveCount;

    public Task DrainRetainedProcessesAsync(CancellationToken cancellationToken = default) =>
        _reaper.DrainAsync(cancellationToken);

    public Task<IReadOnlyList<MsBuildProjectEvaluation>> InspectAsync(
        KarpikSolutionModel solution,
        string engineRoot,
        CancellationToken cancellationToken,
        ProjectInputLease? inputLease = null)
        => InspectAsync(
            solution,
            engineRoot,
            cancellationToken,
            BuildConfiguration.Debug,
            inputLease);

    public async Task<IReadOnlyList<MsBuildProjectEvaluation>> InspectAsync(
        KarpikSolutionModel solution,
        string engineRoot,
        CancellationToken cancellationToken,
        BuildConfiguration configuration,
        ProjectInputLease? inputLease = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        if (!Enum.IsDefined(configuration))
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }
        if (string.IsNullOrWhiteSpace(engineRoot) || !Path.IsPathFullyQualified(engineRoot))
        {
            throw new ArgumentException("Engine root must be absolute.", nameof(engineRoot));
        }

        var results = new List<MsBuildProjectEvaluation>(solution.Projects.Count);
        string solutionRoot = Path.GetDirectoryName(Path.GetFullPath(solution.SolutionPath))
                              ?? throw new ArgumentException(
                                  "Solution path must have a parent directory.",
                                  nameof(solution));
        foreach (KarpikProjectDescriptor project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await InspectProjectAsync(
                project.ProjectPath,
                Path.GetFullPath(engineRoot),
                solutionRoot,
                configuration,
                cancellationToken,
                inputLease));
        }
        return results;
    }

    private async Task<MsBuildProjectEvaluation> InspectProjectAsync(
        string projectPath,
        string engineRoot,
        string solutionRoot,
        BuildConfiguration configuration,
        CancellationToken cancellationToken,
        ProjectInputLease? inputLease)
    {
        string resultPath = Path.Combine(
            Path.GetTempPath(),
            $"karpik-msbuild-evaluation-{Guid.NewGuid():N}.json");
        var startInfo = new ProcessStartInfo
        {
            FileName = _dotNetExecutable,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment.Remove("DOTNET_DiagnosticPorts");
        startInfo.Environment.Remove("DOTNET_DefaultDiagnosticPortSuspend");
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-noAutoResponse");
        startInfo.ArgumentList.Add("-m:1");
        startInfo.ArgumentList.Add("-nr:false");
        startInfo.ArgumentList.Add($"-p:KarpikEngineRoot={engineRoot}");
        startInfo.ArgumentList.Add($"-p:Configuration={configuration}");
        AddScopedImplicitInput(
            startInfo,
            projectPath,
            solutionRoot,
            "Directory.Build.props",
            "DirectoryBuildPropsPath",
            "ImportDirectoryBuildProps");
        AddScopedImplicitInput(
            startInfo,
            projectPath,
            solutionRoot,
            "Directory.Build.targets",
            "DirectoryBuildTargetsPath",
            "ImportDirectoryBuildTargets");
        AddScopedImplicitInput(
            startInfo,
            projectPath,
            solutionRoot,
            "Directory.Packages.props",
            "DirectoryPackagesPropsPath",
            "ImportDirectoryPackagesProps");
        startInfo.ArgumentList.Add(PropertyArgument);
        startInfo.ArgumentList.Add("-getItem:ProjectReference");
        startInfo.ArgumentList.Add(ResultArgumentPrefix + resultPath);

        RetainedProcessAdmission admission = _reaper.Admit();
        IMsBuildProcess? process = null;
        IDisposable? inputRetention = null;
        bool ownershipTransferred = false;
        try
        {
            inputRetention = inputLease?.Retain();
            process = _processFactory.Start(startInfo);
            Task<string> standardOutput = process.ReadStandardOutputAsync(
                MaximumOutputCharacters,
                CancellationToken.None);
            Task<string> standardError = process.ReadStandardErrorAsync(
                MaximumOutputCharacters,
                CancellationToken.None);
            using var timeout = _evaluationTimeout == Timeout.InfiniteTimeSpan
                ? null
                : new CancellationTokenSource(_evaluationTimeout);
            using var linked = timeout is null
                ? null
                : CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked?.Token ?? cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TerminationAttempt termination = await TryTerminateAsync(process);
                if (!termination.ExitConfirmed)
                {
                    _reaper.Retain(
                        admission,
                        process,
                        termination.KillTask,
                        standardOutput,
                        standardError,
                        resultPath,
                        inputRetention);
                    ownershipTransferred = true;
                    inputRetention = null;
                    string diagnostic =
                        $"MSBuild process exit is unconfirmed; background cleanup continues " +
                        $"({_reaper.ActiveCount}/{_reaper.Capacity} retained processes).";
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(diagnostic, cancellationToken);
                    }
                    throw new TimeoutException(diagnostic);
                }
                await DrainAfterTerminationAsync(standardOutput, standardError);
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (timeout is null)
                {
                    throw;
                }
                throw new TimeoutException(
                    $"MSBuild evaluation exceeded {_evaluationTimeout} for '{projectPath}'.");
            }

            string output = await standardOutput;
            string error = await standardError;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"MSBuild evaluation failed for '{projectPath}' with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
            }

            return ReadResult(resultPath, projectPath);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                try
                {
                    if (process is not null)
                    {
                        await process.DisposeAsync();
                    }
                }
                finally
                {
                    TryDeleteResultDirectoryEntry(resultPath);
                    try
                    {
                        inputRetention?.Dispose();
                    }
                    finally
                    {
                        admission.Dispose();
                    }
                }
            }
        }
    }

    private static void AddScopedImplicitInput(
        ProcessStartInfo startInfo,
        string projectPath,
        string solutionRoot,
        string fileName,
        string pathProperty,
        string importProperty)
    {
        string? path = FindNearestInput(
            Path.GetDirectoryName(Path.GetFullPath(projectPath))!,
            Path.GetFullPath(solutionRoot),
            fileName);
        startInfo.ArgumentList.Add(path is null
            ? $"-p:{importProperty}=false"
            : $"-p:{pathProperty}={path}");
    }

    private static string? FindNearestInput(
        string startDirectory,
        string solutionRoot,
        string fileName)
    {
        for (DirectoryInfo? current = new(Path.GetFullPath(startDirectory));
             current is not null;
             current = current.Parent)
        {
            if (!IsWithinRoot(current.FullName, solutionRoot))
            {
                return null;
            }
            string candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            if (PathComparer.Equals(current.FullName, solutionRoot))
            {
                return null;
            }
        }
        return null;
    }

    private static bool IsWithinRoot(string path, string root)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void TryDeleteResultDirectoryEntry(string path)
    {
        try
        {
            // File.Delete removes the directory entry itself, including a broken
            // symbolic link, and does not traverse the link target.
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException)
        {
            // Cleanup is best-effort and must never replace the primary process,
            // timeout, validation, or parsing diagnostic.
        }
    }

    private static async Task DrainAfterTerminationAsync(params Task<string>[] outputTasks)
    {
        try
        {
            await Task.WhenAll(outputTasks);
        }
        catch (Exception exception) when (exception is IOException
                                         or ObjectDisposedException
                                         or InvalidDataException)
        {
            // The primary timeout/cancellation diagnostic owns this path.
        }
    }

    private async Task<TerminationAttempt> TryTerminateAsync(IMsBuildProcess process)
    {
        Task kill = StartKillAsync(process);
        if (await Task.WhenAny(kill, Task.Delay(_terminationTimeout)) != kill)
        {
            return new TerminationAttempt(false, kill);
        }
        await kill;

        bool confirmed = await WaitForExitWithinAsync(process, _terminationTimeout);
        return new TerminationAttempt(confirmed, kill);
    }

    private static Task StartKillAsync(IMsBuildProcess process) => Task.Run(() =>
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException
                                         or System.ComponentModel.Win32Exception)
        {
            // The reaper confirms exit independently and retries boundedly.
        }
    });

    private static async Task<bool> WaitForExitWithinAsync(
        IMsBuildProcess process,
        TimeSpan timeout)
    {
        using var confirmation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(confirmation.Token);
            return true;
        }
        catch (OperationCanceledException) when (confirmation.IsCancellationRequested)
        {
            return false;
        }
    }

    private sealed record TerminationAttempt(bool ExitConfirmed, Task KillTask);

    private sealed class RetainedProcessReaper
    {
        private readonly object _tasksGate = new();
        private readonly HashSet<Task> _tasks = [];
        private readonly SemaphoreSlim _capacity;
        private readonly TimeSpan _retryInterval;
        private int _activeCount;

        public RetainedProcessReaper(int capacity, TimeSpan retryInterval)
        {
            Capacity = capacity;
            _capacity = new SemaphoreSlim(capacity, capacity);
            _retryInterval = retryInterval;
        }

        public int ActiveCount => Volatile.Read(ref _activeCount);
        public int Capacity { get; }

        public RetainedProcessAdmission Admit()
        {
            if (!_capacity.Wait(0))
            {
                throw new InvalidOperationException(
                    $"MSBuild evaluation capacity is exhausted by retained processes " +
                    $"({ActiveCount}/{Capacity}); wait for background cleanup to finish.");
            }
            return new RetainedProcessAdmission(_capacity);
        }

        public void Retain(
            RetainedProcessAdmission admission,
            IMsBuildProcess process,
            Task initialKillTask,
            Task<string> standardOutput,
            Task<string> standardError,
            string resultPath,
            IDisposable? inputRetention)
        {
            Interlocked.Increment(ref _activeCount);
            Task task = Task.Run(() => ReapAsync(
                admission,
                process,
                initialKillTask,
                standardOutput,
                standardError,
                resultPath,
                inputRetention));
            lock (_tasksGate)
            {
                _tasks.Add(task);
            }
            _ = task.ContinueWith(
                completed =>
                {
                    lock (_tasksGate)
                    {
                        _tasks.Remove(completed);
                    }
                    Interlocked.Decrement(ref _activeCount);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public async Task DrainAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                Task[] tasks;
                lock (_tasksGate)
                {
                    tasks = _tasks.ToArray();
                }
                if (tasks.Length == 0)
                {
                    return;
                }
                await Task.WhenAll(tasks).WaitAsync(cancellationToken);
            }
        }

        private async Task ReapAsync(
            RetainedProcessAdmission admission,
            IMsBuildProcess process,
            Task killTask,
            Task<string> standardOutput,
            Task<string> standardError,
            string resultPath,
            IDisposable? inputRetention)
        {
            try
            {
                bool exitConfirmed = false;
                while (!exitConfirmed)
                {
                    try
                    {
                        exitConfirmed = await WaitForExitWithinAsync(
                            process,
                            _retryInterval);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException
                                                     or IOException
                                                     or UnauthorizedAccessException
                                                     or NotSupportedException
                                                     or ObjectDisposedException)
                    {
                        await Task.Delay(_retryInterval);
                    }
                    if (exitConfirmed)
                    {
                        break;
                    }
                    if (killTask.IsCompleted)
                    {
                        await ObserveKillAsync(killTask);
                        killTask = StartKillAsync(process);
                    }
                }

                await ObserveKillAsync(killTask);
                await DrainAfterTerminationAsync(standardOutput, standardError);
            }
            finally
            {
                try
                {
                    await process.DisposeAsync();
                }
                finally
                {
                    TryDeleteResultDirectoryEntry(resultPath);
                    inputRetention?.Dispose();
                    admission.Dispose();
                }
            }
        }

        private static async Task ObserveKillAsync(Task killTask)
        {
            try
            {
                await killTask;
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                             or IOException
                                             or UnauthorizedAccessException
                                             or NotSupportedException
                                             or System.ComponentModel.Win32Exception)
            {
                // Exit confirmation, not Kill success, decides when ownership may be released.
            }
        }
    }

    private sealed class RetainedProcessAdmission(SemaphoreSlim capacity) : IDisposable
    {
        private SemaphoreSlim? _capacity = capacity;

        public void Dispose() =>
            Interlocked.Exchange(ref _capacity, null)?.Release();
    }

    private static MsBuildProjectEvaluation ReadResult(string resultPath, string expectedProjectPath)
    {
        using FileStream stream = OpenNonReparseResultStream(resultPath);

        byte[] buffer = new byte[MaximumResultBytes + 1];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = stream.Read(buffer, length, buffer.Length - length);
            if (read == 0)
            {
                break;
            }
            length += read;
        }
        if (length == 0 || length > MaximumResultBytes)
        {
            throw new InvalidDataException(
                $"MSBuild evaluation result for '{expectedProjectPath}' is empty or exceeds {MaximumResultBytes} bytes.");
        }

        using JsonDocument document = JsonDocument.Parse(buffer.AsMemory(0, length));
        JsonElement properties = document.RootElement.GetProperty("Properties");
        string projectPath = NormalizeRequiredProperty(properties, "MSBuildProjectFullPath");
        if (!PathComparer.Equals(projectPath, Path.GetFullPath(expectedProjectPath)))
        {
            throw new InvalidDataException(
                $"MSBuild evaluated unexpected project '{projectPath}' instead of '{expectedProjectPath}'.");
        }

        var references = new List<string>();
        JsonElement items = document.RootElement.GetProperty("Items");
        foreach (JsonElement reference in items.GetProperty("ProjectReference").EnumerateArray())
        {
            string fullPath = reference.TryGetProperty("FullPath", out JsonElement value)
                ? value.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath))
            {
                throw new InvalidDataException(
                    $"MSBuild returned a ProjectReference without an absolute FullPath for '{projectPath}'.");
            }
            references.Add(Path.GetFullPath(fullPath));
        }

        return new MsBuildProjectEvaluation(
            projectPath,
            ReadProperty(properties, "KarpikProjectKind"),
            ReadProperty(properties, "KarpikSide"),
            ReadProperty(properties, "KarpikRuntimeBundlePath"),
            ReadProperty(properties, "KarpikEngineRoot"),
            ReadProperty(properties, "TargetPath"),
            references,
            ReadProperty(properties, "KarpikCompositionMode"));
    }

    private static FileStream OpenNonReparseResultStream(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            SafeFileHandle handle = NativeResultFile.OpenWithoutFollowingReparsePoint(path);
            try
            {
                if (NativeResultFile.IsReparsePoint(handle))
                {
                    throw new InvalidDataException(
                        $"MSBuild evaluation result must not be a link or reparse point: {path}");
                }
                return new FileStream(handle, FileAccess.Read, bufferSize: 16 * 1024, isAsync: false);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        EnsureResultPathIsNotReparsePoint(path);
        SafeFileHandle unixHandle = NativeResultFile.OpenUnixWithoutFollowingSymbolicLink(path);
        try
        {
            return new FileStream(
                unixHandle,
                FileAccess.Read,
                bufferSize: 16 * 1024,
                isAsync: false);
        }
        catch
        {
            unixHandle.Dispose();
            throw;
        }
    }

    private static void EnsureResultPathIsNotReparsePoint(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || new FileInfo(path).LinkTarget is not null)
        {
            throw new InvalidDataException(
                $"MSBuild evaluation result must not be a link or reparse point: {path}");
        }
    }

    private static class NativeResultFile
    {
        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareDelete = 0x00000004;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint FileFlagSequentialScan = 0x08000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const int FileAttributeTagInfo = 9;
        private const int UnixReadOnly = 0;
        private const int LinuxNoFollow = 0x20000;
        private const int LinuxCloseOnExec = 0x80000;
        private const int MacNoFollow = 0x100;
        private const int MacCloseOnExec = 0x1000000;

        public static SafeFileHandle OpenWithoutFollowingReparsePoint(string path)
        {
            SafeFileHandle handle = CreateFileW(
                path,
                GenericRead,
                FileShareRead | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal | FileFlagSequentialScan | FileFlagOpenReparsePoint,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                if (error is 2 or 3)
                {
                    throw new FileNotFoundException(
                        "MSBuild did not produce an evaluation result.",
                        path);
                }
                throw new IOException(
                    $"Unable to open MSBuild evaluation result '{path}'.",
                    new Win32Exception(error));
            }
            return handle;
        }

        public static bool IsReparsePoint(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileAttributeTagInfo,
                    out FileAttributeTagInformation information,
                    (uint)Marshal.SizeOf<FileAttributeTagInformation>()))
            {
                throw new IOException(
                    "Unable to inspect the opened MSBuild evaluation result handle.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
            return (information.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0;
        }

        public static SafeFileHandle OpenUnixWithoutFollowingSymbolicLink(string path)
        {
            int flags = UnixReadOnly | (OperatingSystem.IsMacOS()
                ? MacNoFollow | MacCloseOnExec
                : LinuxNoFollow | LinuxCloseOnExec);
            int descriptor = Open(path, flags);
            if (descriptor < 0)
            {
                throw new IOException(
                    $"Unable to open MSBuild evaluation result '{path}' without following symbolic links.",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
            }
            return new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            int fileInformationClass,
            out FileAttributeTagInformation fileInformation,
            uint bufferSize);

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int Open(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct FileAttributeTagInformation
        {
            public uint FileAttributes;
            public uint ReparseTag;
        }
    }

    private static string NormalizeRequiredProperty(JsonElement properties, string name)
    {
        string value = ReadProperty(properties, name);
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new InvalidDataException($"MSBuild property '{name}' must be an absolute path.");
        }
        return Path.GetFullPath(value);
    }

    private static string ReadProperty(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

public sealed class SystemMsBuildProcessFactory : IMsBuildProcessFactory
{
    public IMsBuildProcess Start(ProcessStartInfo startInfo)
    {
        if (startInfo.RedirectStandardOutput)
        {
            startInfo.StandardOutputEncoding ??= Encoding.UTF8;
        }
        if (startInfo.RedirectStandardError)
        {
            startInfo.StandardErrorEncoding ??= Encoding.UTF8;
        }

        return new SystemMsBuildProcess(startInfo);
    }
}

internal sealed class SystemMsBuildProcess : IMsBuildProcess
{
    private readonly Process _process;

    public SystemMsBuildProcess(ProcessStartInfo startInfo)
    {
        _process = Process.Start(startInfo)
                   ?? throw new InvalidOperationException("Failed to start dotnet MSBuild evaluation.");
    }

    public int ExitCode => _process.ExitCode;

    public Task<string> ReadStandardOutputAsync(
        int maximumCharacters,
        CancellationToken cancellationToken) =>
        ReadBoundedAsync(_process.StandardOutput, maximumCharacters, cancellationToken);

    public Task<string> ReadStandardErrorAsync(
        int maximumCharacters,
        CancellationToken cancellationToken) =>
        ReadBoundedAsync(_process.StandardError, maximumCharacters, cancellationToken);

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);

    public ValueTask DisposeAsync()
    {
        _process.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 4096));
        char[] buffer = new char[4096];
        bool exceededLimit = false;
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                if (exceededLimit)
                {
                    throw new InvalidDataException(
                        $"MSBuild process output exceeded {maximumCharacters} characters.");
                }
                return builder.ToString();
            }

            if (exceededLimit)
            {
                continue;
            }

            int remaining = maximumCharacters - builder.Length;
            if (read > remaining)
            {
                builder.Append(buffer, 0, remaining);
                exceededLimit = true;
            }
            else
            {
                builder.Append(buffer, 0, read);
            }
        }
    }
}
