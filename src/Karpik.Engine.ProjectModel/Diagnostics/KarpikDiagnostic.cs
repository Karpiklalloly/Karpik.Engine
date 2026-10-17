namespace Karpik.Engine.ProjectModel;

/// <summary>
/// Описывает одно нарушение контракта структуры или зависимостей Karpik-проекта.
/// </summary>
/// <param name="Code">Стабильный код диагностики.</param>
/// <param name="ProjectPath">Путь к проекту-источнику нарушения.</param>
/// <param name="Message">Текст, выводимый пользователю.</param>
/// <param name="Reason">Уточняющая причина нарушения.</param>
public sealed record KarpikDiagnostic(
    string Code,
    string ProjectPath,
    string Message,
    KarpikDiagnosticReason Reason = KarpikDiagnosticReason.None);
