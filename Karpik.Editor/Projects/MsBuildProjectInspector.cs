using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Karpik.Engine.ProjectModel;

namespace Karpik.Editor;

public sealed record MsBuildProjectEvaluation(
    string ProjectPath,
    string Kind,
    string Side,
    string RuntimeBundlePath,
    string EngineRoot,
    string TargetPath,
    IReadOnlyList<string> ProjectReferences);

public interface IMsBuildProjectInspector
{
    Task<IReadOnlyList<MsBuildProjectEvaluation>> InspectAsync(
        KarpikSolutionModel solution,
        string engineRoot,
        CancellationToken cancellationToken);
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
        "-getProperty:MSBuildProjectFullPath,KarpikProjectKind,KarpikSide,KarpikRuntimeBundlePath,KarpikEngineRoot,TargetPath";

    private readonly IMsBuildProcessFactory _processFactory;
    private readonly TimeSpan _evaluationTimeout;
    private readonly TimeSpan _terminationTimeout;
    private readonly string _dotNetExecutable;

    public MsBuildProjectInspector(
        TimeSpan? evaluationTimeout = null,
        TimeSpan? terminationTimeout = null,
        string dotNetExecutable = "dotnet")
        : this(
            new SystemMsBuildProcessFactory(),
            evaluationTimeout ?? TimeSpan.FromSeconds(30),
            terminationTimeout ?? TimeSpan.FromSeconds(5),
            dotNetExecutable)
    {
    }

    public MsBuildProjectInspector(
        IMsBuildProcessFactory processFactory,
        TimeSpan evaluationTimeout,
        TimeSpan terminationTimeout,
        string dotNetExecutable = "dotnet")
    {
        ArgumentNullException.ThrowIfNull(processFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dotNetExecutable);
        if (evaluationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(evaluationTimeout));
        }
        if (terminationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(terminationTimeout));
        }

        _processFactory = processFactory;
        _evaluationTimeout = evaluationTimeout;
        _terminationTimeout = terminationTimeout;
        _dotNetExecutable = dotNetExecutable;
    }

    public async Task<IReadOnlyList<MsBuildProjectEvaluation>> InspectAsync(
        KarpikSolutionModel solution,
        string engineRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        if (string.IsNullOrWhiteSpace(engineRoot) || !Path.IsPathFullyQualified(engineRoot))
        {
            throw new ArgumentException("Engine root must be absolute.", nameof(engineRoot));
        }

        var results = new List<MsBuildProjectEvaluation>(solution.Projects.Count);
        foreach (KarpikProjectDescriptor project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await InspectProjectAsync(
                project.ProjectPath,
                Path.GetFullPath(engineRoot),
                cancellationToken));
        }
        return results;
    }

    private async Task<MsBuildProjectEvaluation> InspectProjectAsync(
        string projectPath,
        string engineRoot,
        CancellationToken cancellationToken)
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
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-m:1");
        startInfo.ArgumentList.Add("-nr:false");
        startInfo.ArgumentList.Add($"-p:KarpikEngineRoot={engineRoot}");
        startInfo.ArgumentList.Add(PropertyArgument);
        startInfo.ArgumentList.Add("-getItem:ProjectReference");
        startInfo.ArgumentList.Add(ResultArgumentPrefix + resultPath);

        await using IMsBuildProcess process = _processFactory.Start(startInfo);
        Task<string> standardOutput = process.ReadStandardOutputAsync(
            MaximumOutputCharacters,
            CancellationToken.None);
        Task<string> standardError = process.ReadStandardErrorAsync(
            MaximumOutputCharacters,
            CancellationToken.None);
        try
        {
            using var timeout = new CancellationTokenSource(_evaluationTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                await TerminateAsync(process);
                await DrainAfterTerminationAsync(standardOutput, standardError);
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }
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

    private async Task TerminateAsync(IMsBuildProcess process)
    {
        Task kill = Task.Run(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between timeout/cancellation and termination.
            }
        });
        if (await Task.WhenAny(kill, Task.Delay(_terminationTimeout)) != kill)
        {
            throw new TimeoutException(
                $"MSBuild process-tree termination did not complete within {_terminationTimeout}.");
        }
        await kill;

        using var confirmation = new CancellationTokenSource(_terminationTimeout);
        try
        {
            await process.WaitForExitAsync(confirmation.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"MSBuild process exit was not confirmed within {_terminationTimeout} after termination.");
        }
    }

    private static MsBuildProjectEvaluation ReadResult(string resultPath, string expectedProjectPath)
    {
        if (!File.Exists(resultPath))
        {
            throw new InvalidDataException($"MSBuild did not produce an evaluation result for '{expectedProjectPath}'.");
        }
        var info = new FileInfo(resultPath);
        if (info.Length is <= 0 or > MaximumResultBytes)
        {
            throw new InvalidDataException(
                $"MSBuild evaluation result for '{expectedProjectPath}' is empty or exceeds {MaximumResultBytes} bytes.");
        }

        byte[] bytes = File.ReadAllBytes(resultPath);
        if (bytes.Length is 0 || bytes.Length > MaximumResultBytes)
        {
            throw new InvalidDataException(
                $"MSBuild evaluation result for '{expectedProjectPath}' changed while reading or exceeded its bound.");
        }

        using JsonDocument document = JsonDocument.Parse(bytes);
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
            references);
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
    public IMsBuildProcess Start(ProcessStartInfo startInfo) =>
        new SystemMsBuildProcess(startInfo);
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
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return builder.ToString();
            }
            if (builder.Length > maximumCharacters - read)
            {
                throw new InvalidDataException(
                    $"MSBuild process output exceeded {maximumCharacters} characters.");
            }
            builder.Append(buffer, 0, read);
        }
    }
}
