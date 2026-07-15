using Karpik.Engine.ProjectModel;
using Microsoft.Build.Framework;

namespace Karpik.Engine.Sdk.Tasks;

public sealed class ValidateKarpikSolutionTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string SolutionPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var model = new KarpikSolutionReader().Read(SolutionPath);
            return LogDiagnostics(new KarpikSolutionValidator().Validate(model));
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Xml.XmlException)
        {
            Log.LogError(
                subcategory: null,
                errorCode: KarpikDiagnosticCodes.InvalidSolutionProject,
                helpKeyword: null,
                file: SolutionPath,
                lineNumber: 0,
                columnNumber: 0,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: $"Unable to validate Karpik solution: {exception.Message}");
            return false;
        }
    }

    private bool LogDiagnostics(IReadOnlyList<KarpikDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            Log.LogError(
                subcategory: null,
                errorCode: diagnostic.Code,
                helpKeyword: null,
                file: diagnostic.ProjectPath,
                lineNumber: 0,
                columnNumber: 0,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: diagnostic.Message);
        }
        return diagnostics.Count == 0;
    }
}
