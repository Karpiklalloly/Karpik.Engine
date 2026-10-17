using Karpik.Engine.Tooling;

namespace Karpik.Editor;

public enum ProjectHandoffCode
{
    NotRequired,
    Requested,
    Failure
}

public sealed record ProjectHandoffResult(ProjectHandoffCode Code, string Message)
{
    public static ProjectHandoffResult NotRequired() =>
        new(ProjectHandoffCode.NotRequired, "The current editor is compatible with the target project.");

    public static ProjectHandoffResult Requested(string message) =>
        new(ProjectHandoffCode.Requested, message);

    public static ProjectHandoffResult Failure(string message) =>
        new(ProjectHandoffCode.Failure, message);
}

public interface IProjectHandoffService
{
    ProjectHandoffResult Prepare(string solutionPath);
}

public sealed class NullProjectHandoffService : IProjectHandoffService
{
    public static NullProjectHandoffService Instance { get; } = new();

    private NullProjectHandoffService()
    {
    }

    public ProjectHandoffResult Prepare(string solutionPath) =>
        ProjectHandoffResult.NotRequired();
}

public sealed class ProjectHandoffService : IProjectHandoffService
{
    private readonly string _handoffPath;
    private readonly string _currentEngineRoot;
    private readonly GlobalJsonSdkVersionReader _globalJsonReader;
    private readonly EngineInstallationValidator _installationValidator;

    public ProjectHandoffService(
        string handoffPath,
        string currentEngineRoot,
        GlobalJsonSdkVersionReader? globalJsonReader = null,
        EngineInstallationValidator? installationValidator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentEngineRoot);
        if (!Path.IsPathFullyQualified(handoffPath) || !Path.IsPathFullyQualified(currentEngineRoot))
        {
            throw new ArgumentException("Handoff and engine-root paths must be absolute.");
        }
        _handoffPath = Path.GetFullPath(handoffPath);
        _currentEngineRoot = Path.GetFullPath(currentEngineRoot);
        _globalJsonReader = globalJsonReader ?? new GlobalJsonSdkVersionReader();
        _installationValidator = installationValidator ?? new EngineInstallationValidator();
    }

    public ProjectHandoffResult Prepare(string solutionPath)
    {
        string solution;
        try
        {
            solution = NormalizeSolutionPath(solutionPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return ProjectHandoffResult.Failure($"Cannot inspect the target project for editor handoff: {exception.Message}");
        }

        GlobalJsonSdkVersionResult sdk = _globalJsonReader.Read(
            Path.Combine(Path.GetDirectoryName(solution)!, "global.json"));
        if (!sdk.IsSuccess || string.IsNullOrWhiteSpace(sdk.SdkVersion))
        {
            return ProjectHandoffResult.Failure(sdk.Message);
        }

        EngineInstallationValidationResult current = _installationValidator.Validate(_currentEngineRoot);
        if (!current.IsValid || current.Manifest is null)
        {
            return ProjectHandoffResult.Failure(
                $"The current editor installation is invalid: {current.Message}");
        }
        if (string.Equals(
                current.Manifest.MsBuildSdkVersion,
                sdk.SdkVersion,
                StringComparison.Ordinal))
        {
            return ProjectHandoffResult.NotRequired();
        }

        try
        {
            new EditorHandoffRequest(solution).Write(_handoffPath);
            return ProjectHandoffResult.Requested(
                $"Project requires Karpik.Engine.Sdk '{sdk.SdkVersion}'; returning it to the launcher.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return ProjectHandoffResult.Failure($"Cannot write editor handoff request: {exception.Message}");
        }
    }

    private static string NormalizeSolutionPath(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        if (!Path.IsPathFullyQualified(solutionPath))
        {
            throw new ArgumentException("Solution path must be absolute.", nameof(solutionPath));
        }
        string path = Path.GetFullPath(solutionPath);
        if (!string.Equals(Path.GetExtension(path), ".slnx", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new FileNotFoundException("Solution must be an existing .slnx file.", path);
        }
        return path;
    }
}
