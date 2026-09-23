using Karpik.Engine.Tooling;

namespace Karpik.Engine.Packager;

/// <summary>Содержит результат materialization и публикации engine payload.</summary>
/// <param name="DestinationDirectory">Каталог опубликованной установки.</param>
/// <param name="ContentHash">Вычисленный хеш содержимого payload.</param>
/// <param name="ReusedExistingInstallation">Указывает, была ли переиспользована идентичная установка.</param>
public sealed record EnginePayloadBuildResult(
    string DestinationDirectory,
    string ContentHash,
    bool ReusedExistingInstallation);

/// <summary>Собирает, проверяет и атомарно публикует versioned engine payload.</summary>
public sealed class EnginePayloadBuilder
{
    private readonly DotNetProcessRunner _processRunner;
    private readonly string? _localApplicationDataRoot;

    /// <summary>Создаёт builder со стандартным runner'ом dotnet-процессов.</summary>
    public EnginePayloadBuilder() : this(new DotNetProcessRunner(), null)
    {
    }

    /// <summary>Создаёт builder с runner'ом для тестирования запуска dotnet.</summary>
    internal EnginePayloadBuilder(DotNetProcessRunner processRunner)
        : this(processRunner, null)
    {
    }

    /// <summary>Создаёт builder с изолированным local-data корнем для тестирования.</summary>
    internal EnginePayloadBuilder(string localApplicationDataRoot)
        : this(new DotNetProcessRunner(), localApplicationDataRoot)
    {
    }

    /// <summary>Создаёт builder с runner'ом и опциональным изолированным local-data корнем.</summary>
    internal EnginePayloadBuilder(DotNetProcessRunner processRunner, string? localApplicationDataRoot)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _localApplicationDataRoot = string.IsNullOrWhiteSpace(localApplicationDataRoot)
            ? null
            : Path.GetFullPath(localApplicationDataRoot);
    }

    /// <summary>Строит payload из checkout или подготовленного layout и публикует его.</summary>
    public EnginePayloadBuildResult Build(
        string sourceRoot,
        string outputRoot,
        string engineVersion,
        string sdkVersion)
    {
        sourceRoot = NormalizeExistingDirectory(sourceRoot, nameof(sourceRoot));
        outputRoot = NormalizeDirectory(outputRoot, nameof(outputRoot));
        ValidateVersion(engineVersion, nameof(engineVersion));
        ValidateVersion(sdkVersion, nameof(sdkVersion));

        var validator = new EngineInstallationValidator();
        var publisher = new AtomicDirectoryPublisher(outputRoot);
        publisher.Recover(path => validator.Validate(path).IsValid);
        string staging = publisher.CreateStagingDirectory();
        try
        {
            PayloadLayout.Materialize(sourceRoot, staging, sdkVersion, _processRunner, _localApplicationDataRoot);
            string contentHash = EngineContentHash.Compute(staging);
            var manifest = new EngineInstallationManifest
            {
                EngineVersion = engineVersion,
                MsBuildSdkVersion = sdkVersion,
                EditorVersion = engineVersion,
                RuntimeProtocolVersion = EngineInstallationManifest.CurrentRuntimeProtocolVersion,
                LayoutVersion = EngineInstallationManifest.CurrentLayoutVersion,
                ContentHash = contentHash
            };
            File.WriteAllText(Path.Combine(staging, PayloadLayout.ManifestFileName), manifest.ToJson());
            File.WriteAllText(Path.Combine(staging, PayloadLayout.CompletionMarkerFileName), "complete" + Environment.NewLine);

            EngineInstallationValidationResult stagingValidation = validator.Validate(staging, sdkVersion, engineVersion);
            if (!stagingValidation.IsValid)
            {
                throw new InvalidDataException($"Staged payload is invalid: {stagingValidation.Message}");
            }

            string destinationName = IsDevelopmentVersion(engineVersion)
                ? $"{engineVersion}-{contentHash}"
                : engineVersion;
            string destination = Path.Combine(outputRoot, "Engines", destinationName);
            if (Directory.Exists(destination))
            {
                EngineInstallationValidationResult existing = validator.Validate(destination, sdkVersion, engineVersion);
                if (existing.IsValid &&
                    string.Equals(existing.Manifest!.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
                {
                    publisher.AbandonStaging(staging);
                    return new EnginePayloadBuildResult(Path.GetFullPath(destination), contentHash, true);
                }
                EngineInstallationValidationResult generallyValid = existing.IsValid
                    ? existing
                    : validator.Validate(destination);
                if (generallyValid.IsValid)
                {
                    throw new InvalidOperationException(
                        $"Engine version '{engineVersion}' is immutable and already contains different valid content at '{destination}'.");
                }
            }

            publisher.Publish(
                staging,
                destination,
                path => validator.Validate(path).IsValid);
            return new EnginePayloadBuildResult(Path.GetFullPath(destination), contentHash, false);
        }
        catch (DotNetProcessTerminationException exception)
        {
            publisher.PreserveStagingAfterUnconfirmedProcess(staging, exception.ProcessId);
            throw;
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                try { publisher.AbandonStaging(staging); } catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException) { }
            }
            throw;
        }
    }

    /// <summary>Нормализует путь и требует существующий каталог.</summary>
    private static string NormalizeExistingDirectory(string path, string parameterName)
    {
        string fullPath = NormalizeDirectory(path, parameterName);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }
        return fullPath;
    }

    /// <summary>Нормализует непустой путь к каталогу.</summary>
    private static string NormalizeDirectory(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        return Path.GetFullPath(path);
    }

    /// <summary>Проверяет версию как безопасный сегмент пути.</summary>
    private static void ValidateVersion(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.IndexOfAny(['/', '\\']) >= 0 ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Version must be a non-empty safe path segment.", parameterName);
        }
    }

    /// <summary>Определяет, требуется ли content-addressed суффикс development-версии.</summary>
    private static bool IsDevelopmentVersion(string version) =>
        version.Contains("-dev", StringComparison.OrdinalIgnoreCase);
}
