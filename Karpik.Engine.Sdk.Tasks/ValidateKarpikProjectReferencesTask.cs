using Karpik.Engine.ProjectModel;
using Microsoft.Build.Framework;

namespace Karpik.Engine.Sdk.Tasks;

public sealed class ValidateKarpikProjectReferencesTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    public ITaskItem[] ProjectReferences { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            var normalizedProjectPath = Path.GetFullPath(ProjectPath);
            var projectDirectory = Path.GetDirectoryName(normalizedProjectPath)!;
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var evaluatedReferences = ProjectReferences
                .Select(reference => Path.GetFullPath(reference.ItemSpec, projectDirectory))
                .Distinct(comparer)
                .ToList();
            var model = new KarpikSolutionReader().ReadProjectGraph(normalizedProjectPath);
            var rootProject = model.Projects.Single(project => comparer.Equals(project.ProjectPath, normalizedProjectPath));
            var rawReferences = rootProject.ProjectReferences.Distinct(comparer).ToList();
            var rawOnly = rawReferences.Except(evaluatedReferences, comparer).OrderBy(path => path, comparer).ToList();
            var evaluatedOnly = evaluatedReferences.Except(rawReferences, comparer).OrderBy(path => path, comparer).ToList();

            if (rawOnly.Count > 0 || evaluatedOnly.Count > 0)
            {
                Log.LogError(
                    subcategory: null,
                    errorCode: KarpikDiagnosticCodes.InvalidSolutionProject,
                    helpKeyword: null,
                    file: normalizedProjectPath,
                    lineNumber: 0,
                    columnNumber: 0,
                    endLineNumber: 0,
                    endColumnNumber: 0,
                    message: "Evaluated ProjectReference items must exactly match static top-level ProjectReference Include entries. " +
                             $"Raw-only: {FormatPaths(rawOnly)}; evaluated-only: {FormatPaths(evaluatedOnly)}.");
                return false;
            }

            var diagnostics = new KarpikSolutionValidator().Validate(model)
                .Where(diagnostic =>
                    diagnostic.Code != KarpikDiagnosticCodes.InvalidSolutionProject ||
                    comparer.Equals(diagnostic.ProjectPath, normalizedProjectPath) ||
                    !diagnostic.Message.StartsWith(
                        "Solution project is missing, unreadable, or outside the solution root:",
                        StringComparison.Ordinal))
                .Distinct()
                .ToList();
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

    private static string FormatPaths(IReadOnlyList<string> paths) =>
        paths.Count == 0 ? "none" : string.Join(", ", paths);
}
