namespace Karpik.Engine.ProjectModel;

/// <summary>Описывает требование проекта к модулю движка.</summary>
/// <param name="Id">Стабильный идентификатор требуемого модуля.</param>
/// <param name="Implementation">Необязательный идентификатор конкретной реализации.</param>
/// <param name="Optional">Указывает, может ли проект работать без модуля.</param>
public sealed record KarpikModuleReference(string Id, string? Implementation, bool Optional);
