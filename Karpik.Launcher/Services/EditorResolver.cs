using Karpik.Engine.Tooling;

namespace Karpik.Launcher.Services;

public enum EditorResolutionCode
{
    Success,
    InvalidSolution,
    GlobalJsonFailure,
    InstallationFailure,
    MissingEditorEntryPoint
}

public sealed record EditorLaunchDescriptor(
    string SolutionPath,
    string SdkVersion,
    string InstallationRoot,
    EngineInstallationManifest Manifest,
    string EditorAssemblyPath,
    string WorkingDirectory,
    string FileName,
    IReadOnlyList<string> PrefixArguments);

public sealed record EditorResolutionResult(
    bool IsSuccess,
    EditorResolutionCode Code,
    string Message,
    EditorLaunchDescriptor? Descriptor = null);

public sealed class EditorResolver
{
    private readonly EngineInstallationResolver _installationResolver;
    private readonly GlobalJsonSdkVersionReader _globalJsonReader;
    private readonly string? _debugEditorDirectory;

    public EditorResolver(
        EngineInstallationResolver? installationResolver = null,
        GlobalJsonSdkVersionReader? globalJsonReader = null,
        string? debugEditorDirectory = null)
    {
        _installationResolver = installationResolver ?? new EngineInstallationResolver();
        _globalJsonReader = globalJsonReader ?? new GlobalJsonSdkVersionReader();
        _debugEditorDirectory = debugEditorDirectory;
    }

    public EditorResolutionResult Resolve(string solutionPath)
    {
        string solution;
        try
        {
            solution = NormalizeSolutionPath(solutionPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return Failure(EditorResolutionCode.InvalidSolution, $"Invalid Karpik solution: {exception.Message}");
        }

        string globalJsonPath = Path.Combine(Path.GetDirectoryName(solution)!, "global.json");
        GlobalJsonSdkVersionResult sdk = _globalJsonReader.Read(globalJsonPath);
        if (!sdk.IsSuccess || string.IsNullOrWhiteSpace(sdk.SdkVersion))
        {
            return Failure(EditorResolutionCode.GlobalJsonFailure, sdk.Message);
        }

        EngineInstallationResolutionResult installation = _installationResolver.Resolve(sdk.SdkVersion);
        if (!installation.IsSuccess || installation.InstallationRoot is null || installation.Manifest is null)
        {
            return Failure(EditorResolutionCode.InstallationFailure, installation.Message);
        }

        string editorRoot = _debugEditorDirectory ?? Path.Combine(installation.InstallationRoot, "editor");
        string assemblyPath = Path.Combine(editorRoot, "Karpik.Editor.dll");
        string platformExecutable = Path.Combine(
            editorRoot,
            OperatingSystem.IsWindows() ? "Karpik.Editor.exe" : "Karpik.Editor");
        string fileName;
        IReadOnlyList<string> prefixArguments;
        if (File.Exists(platformExecutable))
        {
            fileName = platformExecutable;
            prefixArguments = [];
        }
        else if (File.Exists(assemblyPath))
        {
            fileName = "dotnet";
            prefixArguments = [assemblyPath];
        }
        else
        {
            return Failure(
                EditorResolutionCode.MissingEditorEntryPoint,
                $"The selected installation has no Karpik.Editor entry point below '{editorRoot}'.");
        }

        return new EditorResolutionResult(
            true,
            EditorResolutionCode.Success,
            "Compatible editor resolved.",
            new EditorLaunchDescriptor(
                solution,
                sdk.SdkVersion,
                installation.InstallationRoot,
                installation.Manifest,
                assemblyPath,
                editorRoot,
                fileName,
                prefixArguments));
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

    private static EditorResolutionResult Failure(EditorResolutionCode code, string message) =>
        new(false, code, message);
}
