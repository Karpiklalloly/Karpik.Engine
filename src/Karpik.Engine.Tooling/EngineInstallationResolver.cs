using System.Text.Json;

namespace Karpik.Engine.Tooling;

/// <summary>Описывает исход поиска совместимой установки KarpikEngine.</summary>
public enum EngineInstallationResolutionCode
{
    /// <summary>Установка успешно разрешена.</summary>
    Success,
    /// <summary>Запрошенная SDK-версия небезопасна или пуста.</summary>
    InvalidSdkVersion,
    /// <summary>Явно указанный корень установки некорректен.</summary>
    InvalidExplicitRoot,
    /// <summary>Локальное хранилище установок отсутствует.</summary>
    MissingInstallationStore,
    /// <summary>Подходящая установка не найдена.</summary>
    InstallationNotFound,
    /// <summary>Найдено более одной валидной установки для версии SDK.</summary>
    AmbiguousInstallation,
    /// <summary>Найдена, но не прошла проверку совместимая установка.</summary>
    InvalidInstallation
}

/// <summary>Содержит результат поиска engine payload для точной версии SDK.</summary>
/// <param name="IsSuccess">Указывает на успешность поиска.</param>
/// <param name="Code">Код результата.</param>
/// <param name="Message">Текст результата для пользователя или диагностики.</param>
/// <param name="InstallationRoot">Корень найденной установки при успехе.</param>
/// <param name="Manifest">Проверенный manifest найденной установки.</param>
public sealed record EngineInstallationResolutionResult(
    bool IsSuccess,
    EngineInstallationResolutionCode Code,
    string Message,
    string? InstallationRoot = null,
    EngineInstallationManifest? Manifest = null);

public sealed record InstalledEngineInstallation(string InstallationRoot, EngineInstallationManifest Manifest);

/// <summary>Находит единственную валидную установку движка для закреплённой SDK-версии.</summary>
public sealed class EngineInstallationResolver
{
    private readonly EngineInstallationValidator _validator;
    private readonly EngineInstallationValidationCache _validationCache;
    private readonly string _localApplicationDataRoot;

    /// <summary>Создаёт resolver с валидатором и корнем local application data.</summary>
    public EngineInstallationResolver(
        EngineInstallationValidator? validator = null,
        string? localApplicationDataRoot = null,
        EngineInstallationValidationCache? validationCache = null)
    {
        _validator = validator ?? new EngineInstallationValidator();
        _localApplicationDataRoot = Path.GetFullPath(localApplicationDataRoot ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        _validationCache = validationCache ?? new EngineInstallationValidationCache();
    }

    public IReadOnlyList<InstalledEngineInstallation> ListInstalled()
    {
        string store = Path.Combine(_localApplicationDataRoot, "Karpik", "Engines");
        if (!Directory.Exists(store) || PathSafety.IsReparsePoint(store))
        {
            return [];
        }

        return Directory.EnumerateDirectories(store, "*", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: Path.GetFullPath(path), Validation: ValidateCached(path)))
            .Where(item => item.Validation.IsValid && item.Validation.Manifest is not null)
            .Select(item => new InstalledEngineInstallation(item.Path, item.Validation.Manifest!))
            .OrderByDescending(item => item, Comparer<InstalledEngineInstallation>.Create(CompareInstallations))
            .ThenByDescending(item => Directory.GetLastWriteTimeUtc(item.InstallationRoot))
            .ToArray();
    }

    /// <summary>Разрешает явный root либо единственную совместимую установленную версию.</summary>
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

            EngineInstallationValidationResult validation = ValidateCached(explicitRoot, requestedSdkVersion);
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
            EngineInstallationManifest? manifest = TryReadManifest(directory);
            if (manifest is null ||
                !string.Equals(manifest.MsBuildSdkVersion, requestedSdkVersion, StringComparison.Ordinal))
            {
                continue;
            }

            EngineInstallationValidationResult validation = ValidateCached(directory);
            if (validation.IsValid)
            {
                matches.Add((Path.GetFullPath(directory), validation.Manifest!));
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

    /// <summary>Проверяет, пригодна ли версия для использования как сегмент пути.</summary>
    private static bool IsSafeVersion(string version) =>
        !string.IsNullOrWhiteSpace(version) &&
        version is not "." and not ".." &&
        version.IndexOfAny(['/', '\\']) < 0 &&
        version.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static int CompareInstallations(InstalledEngineInstallation left, InstalledEngineInstallation right)
    {
        string[] leftParts = left.Manifest.MsBuildSdkVersion.Split('-', 2);
        string[] rightParts = right.Manifest.MsBuildSdkVersion.Split('-', 2);
        bool leftVersion = Version.TryParse(leftParts[0], out Version? leftCore);
        bool rightVersion = Version.TryParse(rightParts[0], out Version? rightCore);
        if (leftVersion != rightVersion) return leftVersion ? 1 : -1;
        if (leftVersion)
        {
            int core = leftCore!.CompareTo(rightCore);
            if (core != 0) return core;
            bool leftStable = leftParts.Length == 1;
            bool rightStable = rightParts.Length == 1;
            if (leftStable != rightStable) return leftStable ? 1 : -1;
        }
        return string.Compare(left.Manifest.MsBuildSdkVersion, right.Manifest.MsBuildSdkVersion, StringComparison.Ordinal);
    }

    /// <summary>Создаёт успешный результат разрешения.</summary>
    private static EngineInstallationResolutionResult Success(string root, EngineInstallationManifest manifest) =>
        new(true, EngineInstallationResolutionCode.Success, "Engine installation resolved.", root, manifest);

    /// <summary>Создаёт неуспешный результат разрешения.</summary>
    private static EngineInstallationResolutionResult Failure(EngineInstallationResolutionCode code, string message) =>
        new(false, code, message);

    private static EngineInstallationManifest? TryReadManifest(string installationRoot)
    {
        try
        {
            return EngineInstallationManifest.Parse(File.ReadAllText(
                Path.Combine(installationRoot, "engine-installation.json")));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ManifestContractException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private EngineInstallationValidationResult ValidateCached(string installationRoot, string? expectedSdkVersion = null)
    {
        if (_validationCache.TryGetCurrent(installationRoot, out EngineInstallationManifest? manifest))
        {
            if (expectedSdkVersion is not null && !string.Equals(manifest!.MsBuildSdkVersion, expectedSdkVersion, StringComparison.Ordinal))
            {
                return new EngineInstallationValidationResult(
                    false,
                    EngineInstallationValidationCode.WrongSdkVersion,
                    $"Expected MSBuild SDK version '{expectedSdkVersion}', found '{manifest.MsBuildSdkVersion}'.",
                    manifest);
            }

            return new EngineInstallationValidationResult(
                true,
                EngineInstallationValidationCode.Valid,
                "Engine installation is valid.",
                manifest);
        }

        EngineInstallationValidationResult validation = _validator.Validate(installationRoot, expectedSdkVersion);
        if (validation.IsValid && validation.Manifest is not null)
        {
            try
            {
                _validationCache.Record(installationRoot, validation.Manifest);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
            {
                // A missing cache only costs a future full validation; it must not block a valid build.
            }
        }

        return validation;
    }
}
