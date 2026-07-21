using Karpik.Engine.ProjectModel;
using Karpik.Engine.Tooling;

namespace Karpik.Editor;

public interface IProjectOpenService
{
    Task<ProjectOpenResult> OpenAsync(
        string solutionPath,
        ProjectGeneration generation,
        CancellationToken cancellationToken);
}

public sealed record EngineInstallationSelection(
    bool IsSuccess,
    string? EngineRoot,
    string? Diagnostic);

public interface IEngineInstallationProvider
{
    EngineInstallationSelection Resolve(string sdkVersion);
}

public sealed class EngineInstallationProvider : IEngineInstallationProvider
{
    private readonly EngineInstallationResolver _resolver;
    private readonly string? _explicitEngineRoot;

    public EngineInstallationProvider(
        EngineInstallationResolver? resolver = null,
        string? explicitEngineRoot = null)
    {
        _resolver = resolver ?? new EngineInstallationResolver();
        _explicitEngineRoot = explicitEngineRoot
                              ?? Environment.GetEnvironmentVariable("KarpikEngineRoot");
    }

    public EngineInstallationSelection Resolve(string sdkVersion)
    {
        EngineInstallationResolutionResult result = _resolver.Resolve(
            sdkVersion,
            _explicitEngineRoot);
        return new EngineInstallationSelection(
            result.IsSuccess,
            result.InstallationRoot,
            result.IsSuccess ? null : result.Message);
    }
}

public sealed class ProjectOpenService : IProjectOpenService
{
    private readonly IMsBuildProjectInspector _inspector;
    private readonly IEngineInstallationProvider _installationProvider;
    private readonly IActiveProjectContextFactory _contextFactory;
    private readonly IProjectInputLeaseHook? _leaseHook;

    public ProjectOpenService(
        IMsBuildProjectInspector? inspector = null,
        IEngineInstallationProvider? installationProvider = null,
        IActiveProjectContextFactory? contextFactory = null,
        IProjectInputLeaseHook? leaseHook = null)
    {
        _inspector = inspector ?? new MsBuildProjectInspector();
        _installationProvider = installationProvider ?? new EngineInstallationProvider();
        _contextFactory = contextFactory ?? new ActiveProjectContextFactory();
        _leaseHook = leaseHook;
    }

    public async Task<ProjectOpenResult> OpenAsync(
        string solutionPath,
        ProjectGeneration generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!generation.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        string normalizedPath;
        try
        {
            normalizedPath = NormalizeSolutionPath(solutionPath);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or NotSupportedException
                                         or PathTooLongException
                                         or IOException
                                         or UnauthorizedAccessException)
        {
            return ProjectOpenResult.Failure($"Invalid solution path: {exception.Message}");
        }

        ProjectInputLease? inputLease = null;
        try
        {
            inputLease = ProjectInputLease.Acquire(normalizedPath);
            _leaseHook?.AfterLeaseAcquired(normalizedPath);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or NotSupportedException
                                         or PlatformNotSupportedException
                                         or PathTooLongException
                                         or InvalidDataException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or System.Xml.XmlException)
        {
            inputLease?.Dispose();
            return ProjectOpenResult.Failure(
                $"Unable to acquire a stable project input lease: {exception.Message}");
        }
        using ProjectInputLease stableLease = inputLease;
        KarpikSolutionModel solution;
        try
        {
            solution = new KarpikSolutionReader().Read(normalizedPath, stableLease);
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or System.Xml.XmlException)
        {
            return ProjectOpenResult.Failure($"Unable to read solution '{normalizedPath}': {exception.Message}");
        }

        IReadOnlyList<KarpikDiagnostic> rawDiagnostics = new KarpikSolutionValidator().Validate(solution);
        if (rawDiagnostics.Count > 0)
        {
            return ProjectOpenResult.Failure(rawDiagnostics
                .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
                .ToArray());
        }
        if (string.IsNullOrWhiteSpace(solution.SdkVersion))
        {
            return ProjectOpenResult.Failure(
                "global.json must pin an exact Karpik.Engine.Sdk version.");
        }

        EngineInstallationSelection installation = _installationProvider.Resolve(solution.SdkVersion);
        if (!installation.IsSuccess || string.IsNullOrWhiteSpace(installation.EngineRoot))
        {
            return ProjectOpenResult.Failure(
                installation.Diagnostic ?? "The Karpik engine installation could not be resolved.");
        }
        string engineRoot = Path.GetFullPath(installation.EngineRoot);

        IReadOnlyList<MsBuildProjectEvaluation> evaluations;
        try
        {
            KarpikSolutionModel evaluationSolution = stableLease.CreateEvaluationSolution(solution);
            IReadOnlyList<MsBuildProjectEvaluation> rawEvaluations =
                await _inspector.InspectAsync(
                    evaluationSolution,
                    engineRoot,
                    cancellationToken,
                    stableLease);
            evaluations = stableLease.RemapEvaluations(rawEvaluations);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ProjectOpenResult.Failure($"MSBuild evaluation failed: {exception.Message}");
        }

        IReadOnlyList<string> evaluatedDiagnostics = ValidateEvaluations(
            solution,
            evaluations,
            engineRoot,
            Path.GetDirectoryName(solution.SolutionPath)!);
        if (evaluatedDiagnostics.Count > 0)
        {
            return ProjectOpenResult.Failure(evaluatedDiagnostics);
        }

        MsBuildProjectEvaluation client = evaluations.Single(IsClientRuntime);
        MsBuildProjectEvaluation server = evaluations.Single(IsServerRuntime);
        var runtime = new ProjectRuntimeDescriptor(
            engineRoot,
            Path.GetFullPath(client.RuntimeBundlePath),
            Path.GetFullPath(server.RuntimeBundlePath),
            GetRunnerPath(engineRoot, "client"),
            GetRunnerPath(engineRoot, "server"));

        ActiveProjectContext candidate;
        try
        {
            candidate = _contextFactory.Create(solution, runtime, generation);
        }
        catch (Exception exception)
        {
            return ProjectOpenResult.Failure(
                $"Failed to create project context: {exception.Message}");
        }

        return ProjectOpenResult.Success(candidate);
    }

    private static IReadOnlyList<string> ValidateEvaluations(
        KarpikSolutionModel solution,
        IReadOnlyList<MsBuildProjectEvaluation> evaluations,
        string engineRoot,
        string gameRoot)
    {
        var diagnostics = new List<string>();
        var expectedByPath = solution.Projects.ToDictionary(
            project => project.ProjectPath,
            PathComparer);
        var evaluatedByPath = new Dictionary<string, MsBuildProjectEvaluation>(PathComparer);
        foreach (MsBuildProjectEvaluation evaluation in evaluations)
        {
            string path;
            try
            {
                path = Path.GetFullPath(evaluation.ProjectPath);
            }
            catch (Exception exception) when (exception is ArgumentException
                                             or NotSupportedException
                                             or PathTooLongException)
            {
                diagnostics.Add($"MSBuild returned an invalid project path: {exception.Message}");
                continue;
            }
            if (!evaluatedByPath.TryAdd(path, evaluation))
            {
                diagnostics.Add($"MSBuild returned duplicate evaluation for '{path}'.");
            }
        }

        if (expectedByPath.Count != evaluatedByPath.Count ||
            expectedByPath.Keys.Any(path => !evaluatedByPath.ContainsKey(path)))
        {
            diagnostics.Add("MSBuild evaluated project set does not match the raw .slnx project set.");
            return diagnostics;
        }

        foreach ((string path, KarpikProjectDescriptor expected) in expectedByPath)
        {
            MsBuildProjectEvaluation actual = evaluatedByPath[path];
            if (!string.Equals(expected.Kind.ToString(), actual.Kind, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(
                    $"Project '{path}' evaluated KarpikProjectKind '{actual.Kind}', expected '{expected.Kind}'.");
            }
            if (!string.Equals(expected.Side.ToString(), actual.Side, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(
                    $"Project '{path}' evaluated KarpikSide '{actual.Side}', expected '{expected.Side}'.");
            }
            if (!SequenceEqualsAbsolute(expected.ProjectReferences, actual.ProjectReferences))
            {
                diagnostics.Add(
                    $"Project '{path}' evaluated ProjectReference items differ from the raw safe project graph.");
            }
            if (!TryNormalizeAbsolute(actual.EngineRoot, out string evaluatedEngineRoot)
                || !PathComparer.Equals(evaluatedEngineRoot, engineRoot))
            {
                diagnostics.Add(
                    $"Project '{path}' evaluated KarpikEngineRoot outside the selected engine installation.");
            }
        }

        MsBuildProjectEvaluation[] clients = evaluations.Where(IsClientRuntime).ToArray();
        MsBuildProjectEvaluation[] servers = evaluations.Where(IsServerRuntime).ToArray();
        if (clients.Length != 1 || servers.Length != 1)
        {
            diagnostics.Add(
                "A game solution must evaluate exactly one Runtime Client and exactly one Runtime Server project.");
            return diagnostics;
        }
        ValidateBundlePath(clients[0], gameRoot, diagnostics);
        ValidateBundlePath(servers[0], gameRoot, diagnostics);
        ValidateTargetPath(clients[0], diagnostics);
        ValidateTargetPath(servers[0], diagnostics);
        return diagnostics;
    }

    private static void ValidateBundlePath(
        MsBuildProjectEvaluation evaluation,
        string gameRoot,
        ICollection<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(evaluation.RuntimeBundlePath) ||
            !Path.IsPathFullyQualified(evaluation.RuntimeBundlePath))
        {
            diagnostics.Add(
                $"Project '{evaluation.ProjectPath}' must evaluate an absolute KarpikRuntimeBundlePath.");
            return;
        }
        if (!IsWithinRoot(evaluation.RuntimeBundlePath, gameRoot))
        {
            diagnostics.Add(
                $"Project '{evaluation.ProjectPath}' evaluates KarpikRuntimeBundlePath outside the active game root.");
        }
    }

    private static bool IsWithinRoot(string candidate, string root)
    {
        string normalizedCandidate = Path.GetFullPath(candidate);
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return normalizedCandidate.Equals(normalizedRoot, PathComparison) ||
               normalizedCandidate.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   PathComparison);
    }

    private static void ValidateTargetPath(
        MsBuildProjectEvaluation evaluation,
        ICollection<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(evaluation.TargetPath) ||
            !Path.IsPathFullyQualified(evaluation.TargetPath))
        {
            diagnostics.Add(
                $"Project '{evaluation.ProjectPath}' must evaluate an absolute TargetPath.");
        }
    }

    private static bool TryNormalizeAbsolute(string? path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }
        try
        {
            normalized = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or NotSupportedException
                                         or PathTooLongException)
        {
            return false;
        }
    }

    private static bool SequenceEqualsAbsolute(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual)
    {
        string[] expectedPaths = expected
            .Select(Path.GetFullPath)
            .Order(PathComparer)
            .ToArray();
        string[] actualPaths = actual
            .Select(Path.GetFullPath)
            .Order(PathComparer)
            .ToArray();
        return expectedPaths.SequenceEqual(actualPaths, PathComparer);
    }

    private static bool IsClientRuntime(MsBuildProjectEvaluation evaluation) =>
        string.Equals(evaluation.Kind, nameof(KarpikProjectKind.Runtime), StringComparison.OrdinalIgnoreCase)
        && string.Equals(evaluation.Side, nameof(KarpikProjectSide.Client), StringComparison.OrdinalIgnoreCase);

    private static bool IsServerRuntime(MsBuildProjectEvaluation evaluation) =>
        string.Equals(evaluation.Kind, nameof(KarpikProjectKind.Runtime), StringComparison.OrdinalIgnoreCase)
        && string.Equals(evaluation.Side, nameof(KarpikProjectSide.Server), StringComparison.OrdinalIgnoreCase);

    private static string GetRunnerPath(string engineRoot, string side)
    {
        string directory = Path.Combine(engineRoot, "runners", side);
        string executable = Path.Combine(
            directory,
            OperatingSystem.IsWindows()
                ? "Karpik.Engine.Core.Runner.exe"
                : "Karpik.Engine.Core.Runner");
        if (File.Exists(executable))
        {
            return executable;
        }

        // Valid installations are required to contain the managed runner. Milestone 6B
        // may choose a dotnet host when a platform apphost is intentionally absent.
        string managed = Path.Combine(directory, "Karpik.Engine.Core.Runner.dll");
        return File.Exists(managed) ? managed : executable;
    }

    private static string NormalizeSolutionPath(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        string fullPath = Path.GetFullPath(solutionPath);
        if (!Path.IsPathFullyQualified(solutionPath) ||
            !string.Equals(Path.GetExtension(fullPath), ".slnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Project path must be an absolute .slnx file.", nameof(solutionPath));
        }
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Solution file was not found.", fullPath);
        }
        return fullPath;
    }

    private static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison { get; } = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
