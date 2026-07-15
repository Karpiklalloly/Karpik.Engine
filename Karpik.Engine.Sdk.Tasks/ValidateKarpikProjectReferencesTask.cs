using Karpik.Engine.ProjectModel;
using Microsoft.Build.Framework;

namespace Karpik.Engine.Sdk.Tasks;

public sealed class ValidateKarpikProjectReferencesTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var model = new KarpikSolutionReader().ReadProjectGraph(ProjectPath);
            var diagnostics = new KarpikSolutionValidator().Validate(model);
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
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Xml.XmlException)
        {
            Log.LogError(
                subcategory: null,
                errorCode: KarpikDiagnosticCodes.InvalidSolutionProject,
                helpKeyword: null,
                file: ProjectPath,
                lineNumber: 0,
                columnNumber: 0,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: $"Unable to validate Karpik project graph: {exception.Message}");
            return false;
        }
    }
}
