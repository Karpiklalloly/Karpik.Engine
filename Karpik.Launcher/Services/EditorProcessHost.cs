using System.Diagnostics;
using Karpik.Engine.Tooling;

namespace Karpik.Launcher.Services;

public sealed record EditorProcessStartRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);

public interface IEditorProcessRunner
{
    Task<int> RunAsync(EditorProcessStartRequest request, CancellationToken cancellationToken);
}

public sealed class SystemEditorProcessRunner : IEditorProcessRunner
{
    public async Task<int> RunAsync(
        EditorProcessStartRequest request,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false
        };
        foreach (string argument in request.Arguments) startInfo.ArgumentList.Add(argument);
        foreach ((string name, string value) in request.Environment) startInfo.Environment[name] = value;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start editor process '{request.FileName}'.");
        }
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        return process.ExitCode;
    }
}

public enum EditorHostCode
{
    Success,
    ResolutionFailure,
    ProcessFailure,
    EditorFailed,
    InvalidHandoff,
    HandoffLimitExceeded
}

public sealed record EditorHostResult(
    bool IsSuccess,
    EditorHostCode Code,
    string Message,
    string SolutionPath,
    int? EditorExitCode = null);

public interface IEditorProcessHost
{
    Task<EditorHostResult> RunAsync(
        string solutionPath,
        CancellationToken cancellationToken = default);
}

public sealed class EditorProcessHost : IEditorProcessHost
{
    private readonly EditorResolver _resolver;
    private readonly IEditorProcessRunner _processRunner;
    private readonly string _handoffRoot;
    private readonly int _maximumHandoffs;

    public EditorProcessHost(
        EditorResolver? resolver = null,
        IEditorProcessRunner? processRunner = null,
        string? localApplicationDataRoot = null,
        int maximumHandoffs = 4)
    {
        if (maximumHandoffs < 0) throw new ArgumentOutOfRangeException(nameof(maximumHandoffs));
        _resolver = resolver ?? new EditorResolver();
        _processRunner = processRunner ?? new SystemEditorProcessRunner();
        string localRoot = Path.GetFullPath(localApplicationDataRoot ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        _handoffRoot = Path.Combine(localRoot, "Karpik", "Launcher", "handoff");
        _maximumHandoffs = maximumHandoffs;
    }

    public async Task<EditorHostResult> RunAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        string currentSolution = solutionPath;
        int completedHandoffs = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EditorResolutionResult resolution = _resolver.Resolve(currentSolution);
            if (!resolution.IsSuccess || resolution.Descriptor is null)
            {
                return Failure(EditorHostCode.ResolutionFailure, resolution.Message, currentSolution);
            }
            EditorLaunchDescriptor descriptor = resolution.Descriptor;

            string requestDirectory = Path.Combine(_handoffRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(requestDirectory);
            string handoffPath = Path.Combine(requestDirectory, "handoff.json");
            try
            {
                var arguments = new List<string>(descriptor.PrefixArguments.Count + 4);
                arguments.AddRange(descriptor.PrefixArguments);
                arguments.Add("--solution");
                arguments.Add(descriptor.SolutionPath);
                arguments.Add("--handoff");
                arguments.Add(handoffPath);
                var request = new EditorProcessStartRequest(
                    descriptor.FileName,
                    arguments,
                    Path.Combine(descriptor.InstallationRoot, "editor"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["KarpikEngineRoot"] = descriptor.InstallationRoot
                    });

                int exitCode;
                try
                {
                    exitCode = await _processRunner.RunAsync(request, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return Failure(
                        EditorHostCode.ProcessFailure,
                        $"Editor process failed: {exception.Message}",
                        descriptor.SolutionPath);
                }

                if (exitCode == 0)
                {
                    return new EditorHostResult(
                        true,
                        EditorHostCode.Success,
                        "Editor closed normally.",
                        descriptor.SolutionPath,
                        exitCode);
                }
                if (exitCode != EditorExitCodes.HandoffRequested)
                {
                    return Failure(
                        EditorHostCode.EditorFailed,
                        $"Editor exited with code {exitCode}.",
                        descriptor.SolutionPath,
                        exitCode);
                }
                if (completedHandoffs >= _maximumHandoffs)
                {
                    return Failure(
                        EditorHostCode.HandoffLimitExceeded,
                        $"Editor handoff limit of {_maximumHandoffs} was exceeded.",
                        descriptor.SolutionPath,
                        exitCode);
                }

                try
                {
                    EditorHandoffRequest handoff = EditorHandoffRequest.Read(handoffPath);
                    currentSolution = handoff.SolutionPath;
                    completedHandoffs++;
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    return Failure(
                        EditorHostCode.InvalidHandoff,
                        $"Editor handoff is invalid: {exception.Message}",
                        descriptor.SolutionPath,
                        exitCode);
                }
            }
            finally
            {
                TryDelete(handoffPath);
                TryDeleteEmptyDirectory(requestDirectory);
            }
        }
    }

    private static EditorHostResult Failure(
        EditorHostCode code,
        string message,
        string solutionPath,
        int? exitCode = null) =>
        new(false, code, message, solutionPath ?? string.Empty, exitCode);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
