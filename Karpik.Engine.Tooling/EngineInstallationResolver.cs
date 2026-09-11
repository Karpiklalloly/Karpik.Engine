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

/// <summary>Находит единственную валидную установку движка для закреплённой SDK-версии.</summary>
public sealed class EngineInstallationResolver
{
    private readonly EngineInstallationValidator _validator;
    private readonly string _localApplicationDataRoot;

    /// <summary>Создаёт resolver с валидатором и корнем local application data.</summary>
    public EngineInstallationResolver(
        EngineInstallationValidator? validator = null,
        string? localApplicationDataRoot = null)
    {
        _validator = validator ?? new EngineInstallationValidator();
        _localApplicationDataRoot = Path.GetFullPath(localApplicationDataRoot ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
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

    /// <summary>Проверяет, пригодна ли версия для использования как сегмент пути.</summary>
    private static bool IsSafeVersion(string version) =>
        !string.IsNullOrWhiteSpace(version) &&
        version is not "." and not ".." &&
        version.IndexOfAny(['/', '\\']) < 0 &&
        version.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Создаёт успешный результат разрешения.</summary>
    private static EngineInstallationResolutionResult Success(string root, EngineInstallationManifest manifest) =>
        new(true, EngineInstallationResolutionCode.Success, "Engine installation resolved.", root, manifest);

    /// <summary>Создаёт неуспешный результат разрешения.</summary>
    private static EngineInstallationResolutionResult Failure(EngineInstallationResolutionCode code, string message) =>
        new(false, code, message);
}
