using Karpik.Engine.ProjectModel;
using Microsoft.Build.Framework;

namespace Karpik.Engine.Sdk.Tasks;

/// <summary>
/// MSBuild-задача, которая читает и проверяет полный граф проектов Karpik-решения.
/// </summary>
public sealed class ValidateKarpikSolutionTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Получает абсолютный путь к файлу решения, подлежащему проверке.
    /// </summary>
    [Required]
    public string SolutionPath { get; set; } = string.Empty;

    /// <summary>
    /// Выполняет чтение решения и передаёт найденные нарушения в журнал MSBuild.
    /// </summary>
    /// <returns><see langword="true"/>, если нарушений не найдено; иначе <see langword="false"/>.</returns>
    public override bool Execute()
    {
        try
        {
            KarpikSolutionModel model = new KarpikSolutionReader().Read(SolutionPath);
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

    /// <summary>
    /// Записывает диагностические сообщения модели проекта в журнал MSBuild.
    /// </summary>
    /// <param name="diagnostics">Диагностики, которые необходимо вывести.</param>
    /// <returns><see langword="true"/>, если список пуст; иначе <see langword="false"/>.</returns>
    private bool LogDiagnostics(IReadOnlyList<KarpikDiagnostic> diagnostics)
    {
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
}
