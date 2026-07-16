using Karpik.Engine.ProjectModel;
using Karpik.Engine.Tooling;
using System.Xml;
using System.Xml.Linq;

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

    public ProjectOpenService(
        IMsBuildProjectInspector? inspector = null,
        IEngineInstallationProvider? installationProvider = null,
        IActiveProjectContextFactory? contextFactory = null)
    {
        _inspector = inspector ?? new MsBuildProjectInspector();
        _installationProvider = installationProvider ?? new EngineInstallationProvider();
        _contextFactory = contextFactory ?? new ActiveProjectContextFactory();
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
            ValidateRawPathSafety(normalizedPath);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or NotSupportedException
                                         or PathTooLongException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or XmlException)
        {
            return ProjectOpenResult.Failure($"Invalid solution path: {exception.Message}");
        }

        KarpikSolutionModel solution;
        try
        {
            solution = new KarpikSolutionReader().Read(normalizedPath);
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

        try
        {
            // Close the validation/evaluation race window as far as path-based .NET APIs
            // allow: a project tree changed to a link after raw reading is rejected before
            // the first child process starts.
            ValidateRawPathSafety(normalizedPath);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or NotSupportedException
                                         or PathTooLongException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or XmlException)
        {
            return ProjectOpenResult.Failure(
                $"Project paths changed or are unsafe before MSBuild evaluation: {exception.Message}");
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
            evaluations = await _inspector.InspectAsync(solution, engineRoot, cancellationToken);
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
            engineRoot);
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
        string engineRoot)
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
        ValidateBundlePath(clients[0], diagnostics);
        ValidateBundlePath(servers[0], diagnostics);
        ValidateTargetPath(clients[0], diagnostics);
        ValidateTargetPath(servers[0], diagnostics);
        return diagnostics;
    }

    private static void ValidateBundlePath(
        MsBuildProjectEvaluation evaluation,
        ICollection<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(evaluation.RuntimeBundlePath) ||
            !Path.IsPathFullyQualified(evaluation.RuntimeBundlePath))
        {
            diagnostics.Add(
                $"Project '{evaluation.ProjectPath}' must evaluate an absolute KarpikRuntimeBundlePath.");
        }
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

    private static void ValidateRawPathSafety(string solutionPath)
    {
        string root = Path.GetDirectoryName(solutionPath)
                      ?? throw new InvalidDataException("Solution path has no parent directory.");
        EnsureAncestorChainHasNoReparsePoints(solutionPath);
        XDocument solution = LoadSafeXml(solutionPath);
        foreach (string declaredPath in solution.Descendants()
                     .Where(element => element.Name.LocalName == "Project")
                     .Select(element => element.Attributes()
                         .FirstOrDefault(attribute => attribute.Name.LocalName == "Path")?.Value)
                     .Where(path => !string.IsNullOrWhiteSpace(path))!)
        {
            string projectPath = Path.GetFullPath(declaredPath, root);
            EnsurePathWithinRootIsSafe(projectPath, root, "solution project");
            if (!File.Exists(projectPath))
            {
                continue;
            }

            XDocument project = LoadSafeXml(projectPath);
            string projectDirectory = Path.GetDirectoryName(projectPath)!;
            foreach (XElement reference in project.Root?.Elements()
                         .Where(element => element.Name.LocalName == "ItemGroup")
                         .SelectMany(group => group.Elements()
                             .Where(element => element.Name.LocalName == "ProjectReference"))
                     ?? [])
            {
                string? include = reference.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName == "Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }
                string referencePath = Path.GetFullPath(include, projectDirectory);
                EnsurePathWithinRootIsSafe(referencePath, root, "raw ProjectReference");
            }
        }
    }

    private static XDocument LoadSafeXml(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using XmlReader reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static void EnsureAncestorChainHasNoReparsePoints(string path)
    {
        FileSystemInfo? current = File.Exists(path)
            ? new FileInfo(path)
            : new DirectoryInfo(path);
        while (current is not null)
        {
            EnsureNotReparsePoint(current.FullName);
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }
    }

    private static void EnsurePathWithinRootIsSafe(
        string path,
        string root,
        string description)
    {
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(normalizedRoot, normalizedPath);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{description} escapes the solution root: {normalizedPath}");
        }

        EnsureNotReparsePoint(normalizedRoot);
        if (relative == ".")
        {
            return;
        }
        string current = normalizedRoot;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            EnsureNotReparsePoint(current);
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"Project path contains a link or reparse point: {path}");
                }
            }

            string? fileTarget = new FileInfo(path).LinkTarget;
            string? directoryTarget = new DirectoryInfo(path).LinkTarget;
            if (fileTarget is not null || directoryTarget is not null)
            {
                throw new InvalidDataException(
                    $"Project path contains a link or reparse point: {path}");
            }
        }
        catch (FileNotFoundException)
        {
            // Missing paths remain the responsibility of raw structural validation.
        }
        catch (DirectoryNotFoundException)
        {
            // Missing paths remain the responsibility of raw structural validation.
        }
    }

    private static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
