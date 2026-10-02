namespace Karpik.Engine.ProjectModel;

/// <summary>
/// Уточняет категорию нарушения, использованную при построении диагностики Karpik.
/// </summary>
public enum KarpikDiagnosticReason
{
    /// <summary>Дополнительная категория не задана.</summary>
    None,
    /// <summary>Запись проекта в решении или графе некорректна.</summary>
    InvalidProjectEntry,
    /// <summary>Один проект объявлен более одного раза.</summary>
    DuplicateProjectEntry,
    /// <summary>Ссылка на проект указывает на недопустимую цель.</summary>
    InvalidProjectReference,
    /// <summary>Синтаксис ссылки на проект не поддерживается статическим валидатором.</summary>
    UnsupportedProjectReferenceSyntax
}
