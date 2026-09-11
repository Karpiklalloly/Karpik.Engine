using Karpik.Engine.ProjectModel;
using Karpik.Engine.Tooling;
using Microsoft.Build.Framework;

namespace Karpik.Engine.Sdk.Tasks;

/// <summary>
/// MSBuild-задача, разрешающая совместимую установку KarpikEngine для закреплённой версии SDK.
/// </summary>
public sealed class ResolveKarpikEngineRootTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Получает точную версию MSBuild SDK, которой должна соответствовать установка движка.
    /// </summary>
    [Required]
    public string SdkVersion { get; set; } = string.Empty;

    /// <summary>
    /// Получает или задаёт явный абсолютный путь к установке движка вместо автоматического поиска.
    /// </summary>
    public string? ExplicitRoot { get; set; }

    /// <summary>
    /// Получает или задаёт корень local application data для изолированного поиска установки.
    /// </summary>
    public string? LocalApplicationDataRoot { get; set; }

    /// <summary>
    /// Получает разрешённый абсолютный путь к валидированной установке движка.
    /// </summary>
    [Output]
    public string ResolvedRoot { get; set; } = string.Empty;

    /// <summary>
    /// Разрешает установку и сообщает в MSBuild диагностическую ошибку при неудаче.
    /// </summary>
    /// <returns><see langword="true"/>, если <see cref="ResolvedRoot"/> установлен; иначе <see langword="false"/>.</returns>
    public override bool Execute()
    {
        try
        {
            string? localApplicationDataRoot = string.IsNullOrWhiteSpace(LocalApplicationDataRoot)
                ? null
                : LocalApplicationDataRoot;
            EngineInstallationResolver resolver = new EngineInstallationResolver(
                localApplicationDataRoot: localApplicationDataRoot);
            EngineInstallationResolutionResult result = resolver.Resolve(SdkVersion, ExplicitRoot);
            if (!result.IsSuccess)
            {
                LogResolutionError(result.Message);
                return false;
            }

            ResolvedRoot = result.InstallationRoot!;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                IOException or
                UnauthorizedAccessException or
                NotSupportedException)
        {
            LogResolutionError($"Unable to resolve the KarpikEngine installation: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Записывает ошибку разрешения установки с кодом диагностики Karpik.
    /// </summary>
    /// <param name="message">Текст ошибки.</param>
    private void LogResolutionError(string message)
    {
        Log.LogError(
            subcategory: null,
            errorCode: KarpikDiagnosticCodes.EngineInstallationResolutionFailed,
            helpKeyword: null,
            file: null,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message: message);
    }
}
