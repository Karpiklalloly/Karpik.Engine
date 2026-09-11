using Karpik.Engine.ProjectModel;
using Microsoft.Build.Framework;

namespace Karpik.Engine.Sdk.Tasks;

/// <summary>
/// MSBuild-задача, которая сверяет вычисленные <c>ProjectReference</c> с исходным
/// статическим графом и проверяет границы Karpik-проектов.
/// </summary>
public sealed class ValidateKarpikProjectReferencesTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Получает путь к корневому проекту проверяемого графа.
    /// </summary>
    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Получает вычисленные MSBuild ссылки на проекты для сверки с исходным XML.
    /// </summary>
    public ITaskItem[] ProjectReferences { get; set; } = [];

    /// <summary>
    /// Выполняет сверку ссылок и записывает нарушения графа в журнал MSBuild.
    /// </summary>
    /// <returns><see langword="true"/>, если граф корректен; иначе <see langword="false"/>.</returns>
    public override bool Execute()
    {
        try
        {
            string normalizedProjectPath = Path.GetFullPath(ProjectPath);
            string projectDirectory = Path.GetDirectoryName(normalizedProjectPath)!;
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            List<string> evaluatedReferences = ProjectReferences
                .Select(reference => Path.GetFullPath(reference.ItemSpec, projectDirectory))
                .Distinct(comparer)
                .ToList();
            KarpikSolutionModel model = new KarpikSolutionReader().ReadProjectGraph(normalizedProjectPath);
            KarpikProjectDescriptor rootProject = model.Projects.Single(project => comparer.Equals(project.ProjectPath, normalizedProjectPath));
            List<string> rawReferences = rootProject.ProjectReferences.Distinct(comparer).ToList();
            List<string> rawOnly = rawReferences.Except(evaluatedReferences, comparer).OrderBy(path => path, comparer).ToList();
            List<string> evaluatedOnly = evaluatedReferences.Except(rawReferences, comparer).OrderBy(path => path, comparer).ToList();

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

            List<KarpikDiagnostic> diagnostics = new KarpikSolutionValidator().Validate(model)
                .Where(diagnostic =>
                    diagnostic.Reason != KarpikDiagnosticReason.InvalidProjectEntry ||
                    comparer.Equals(diagnostic.ProjectPath, normalizedProjectPath))
                .Distinct()
                .ToList();
            foreach (KarpikDiagnostic diagnostic in diagnostics)
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

    /// <summary>
    /// Преобразует список путей в стабильное текстовое представление для диагностики.
    /// </summary>
    /// <param name="paths">Пути для форматирования.</param>
    /// <returns>Список путей через запятую либо <c>none</c> для пустого списка.</returns>
    private static string FormatPaths(IReadOnlyList<string> paths) =>
        paths.Count == 0 ? "none" : string.Join(", ", paths);
}
