namespace Karpik.Engine.Tooling;

public enum EngineInstallationResolutionCode
{
    Success,
    InvalidSdkVersion,
    InvalidExplicitRoot,
    MissingInstallationStore,
    InstallationNotFound,
    AmbiguousInstallation,
    InvalidInstallation
}

public sealed record EngineInstallationResolutionResult(
    bool IsSuccess,
    EngineInstallationResolutionCode Code,
    string Message,
    string? InstallationRoot = null,
    EngineInstallationManifest? Manifest = null);

public sealed class EngineInstallationResolver
{
    private readonly EngineInstallationValidator _validator;
    private readonly string _localApplicationDataRoot;

    public EngineInstallationResolver(
        EngineInstallationValidator? validator = null,
        string? localApplicationDataRoot = null)
    {
        _validator = validator ?? new EngineInstallationValidator();
        _localApplicationDataRoot = Path.GetFullPath(localApplicationDataRoot ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    public EngineInstallationResolutionResult Resolve(
        string requestedSdkVersion,
        string? karpikEngineRoot = null)
    {
        if (!IsSafeVersion(requestedSdkVersion))
        {
            return Failure(EngineInstallationResolutionCode.InvalidSdkVersion, $"Invalid Karpik.Engine.Sdk version '{requestedSdkVersion}'.");
        }

        if (!string.IsNullOrWhiteSpace(karpikEngineRoot))
        {
            string explicitRoot;
            try
            {
                if (!Path.IsPathFullyQualified(karpikEngineRoot))
                {
                    return Failure(EngineInstallationResolutionCode.InvalidExplicitRoot, "KarpikEngineRoot must be an absolute path.");
                }
                explicitRoot = Path.GetFullPath(karpikEngineRoot);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Failure(EngineInstallationResolutionCode.InvalidExplicitRoot, $"Invalid KarpikEngineRoot: {exception.Message}");
            }

            EngineInstallationValidationResult validation = _validator.Validate(explicitRoot, requestedSdkVersion);
            if (!validation.IsValid)
            {
                return Failure(EngineInstallationResolutionCode.InvalidInstallation, $"KarpikEngineRoot is not a valid installation: {validation.Message}");
            }
            return Success(explicitRoot, validation.Manifest!);
        }

        string store = Path.Combine(_localApplicationDataRoot, "Karpik", "Engines");
        if (!Directory.Exists(store))
        {
            return Failure(EngineInstallationResolutionCode.MissingInstallationStore, $"Engine installation store does not exist: {store}");
        }
        if (PathSafety.IsReparsePoint(store))
        {
            return Failure(EngineInstallationResolutionCode.InvalidInstallation, $"Engine installation store is a link or reparse point: {store}");
        }

        var matches = new List<(string Root, EngineInstallationManifest Manifest)>();
        var invalidMatches = new List<EngineInstallationValidationResult>();
        foreach (string directory in Directory.EnumerateDirectories(store, "*", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            EngineInstallationValidationResult validation = _validator.Validate(directory);
            if (validation.Manifest is null ||
                !string.Equals(validation.Manifest.MsBuildSdkVersion, requestedSdkVersion, StringComparison.Ordinal))
            {
                continue;
            }
            if (validation.IsValid)
            {
                matches.Add((Path.GetFullPath(directory), validation.Manifest));
            }
            else
            {
                invalidMatches.Add(validation);
            }
        }

        if (matches.Count > 1)
        {
            string names = string.Join(", ", matches
                .Select(match => Path.GetFileName(match.Root))
                .Order(StringComparer.Ordinal));
            return Failure(
                EngineInstallationResolutionCode.AmbiguousInstallation,
                $"Multiple valid engine installations provide exact SDK version '{requestedSdkVersion}': {names}.");
        }
        if (matches.Count == 1)
        {
            return Success(matches[0].Root, matches[0].Manifest);
        }
        if (invalidMatches.Count > 0)
        {
            return Failure(EngineInstallationResolutionCode.InvalidInstallation, $"An installation for SDK version '{requestedSdkVersion}' exists but is invalid: {invalidMatches[0].Message}");
        }
        return Failure(EngineInstallationResolutionCode.InstallationNotFound, $"No valid engine installation provides exact SDK version '{requestedSdkVersion}'.");
    }

    private static bool IsSafeVersion(string version) =>
        !string.IsNullOrWhiteSpace(version) &&
        version is not "." and not ".." &&
        version.IndexOfAny(['/', '\\']) < 0 &&
        version.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static EngineInstallationResolutionResult Success(string root, EngineInstallationManifest manifest) =>
        new(true, EngineInstallationResolutionCode.Success, "Engine installation resolved.", root, manifest);

    private static EngineInstallationResolutionResult Failure(EngineInstallationResolutionCode code, string message) =>
        new(false, code, message);
}
