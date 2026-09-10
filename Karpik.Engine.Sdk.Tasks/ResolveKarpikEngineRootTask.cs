using Karpik.Engine.ProjectModel;
using Karpik.Engine.Tooling;
using Microsoft.Build.Framework;

namespace Karpik.Engine.Sdk.Tasks;

public sealed class ResolveKarpikEngineRootTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string SdkVersion { get; set; } = string.Empty;

    public string? ExplicitRoot { get; set; }

    public string? LocalApplicationDataRoot { get; set; }

    [Output]
    public string ResolvedRoot { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            string? localApplicationDataRoot = string.IsNullOrWhiteSpace(LocalApplicationDataRoot)
                ? null
                : LocalApplicationDataRoot;
            EngineInstallationResolver resolver = new EngineInstallationResolver(
                localApplicationDataRoot: localApplicationDataRoot);
            EngineInstallationResolutionResult result = resolver.Resolve(
                SdkVersion,
                ExplicitRoot);
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
