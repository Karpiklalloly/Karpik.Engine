namespace Karpik.Engine.ProjectModel;

/// <summary>Описывает один проект, извлечённый из Karpik-решения.</summary>
/// <param name="ProjectPath">Нормализованный абсолютный путь к проекту.</param>
/// <param name="SdkNames">Имена SDK, объявленные проектом.</param>
/// <param name="Kind">Роль проекта в Karpik-сборке.</param>
/// <param name="Side">Сторона runtime-границы проекта.</param>
/// <param name="ProjectReferences">Статические ссылки на другие проекты.</param>
/// <param name="Modules">Требуемые модули движка.</param>
public sealed record KarpikProjectDescriptor(
    string ProjectPath,
    IReadOnlyList<string> SdkNames,
    KarpikProjectKind Kind,
    KarpikProjectSide Side,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<KarpikModuleReference> Modules)
{
    /// <summary>Получает результат чтения project-файла для внутренней валидации.</summary>
    internal KarpikProjectReadStatus ReadStatus { get; init; }
    /// <summary>Определяет, были ли все project references объявлены статически.</summary>
    internal bool ProjectReferencesAreStatic { get; init; }
}
