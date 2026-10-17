namespace Karpik.Engine.ProjectModel;

/// <summary>Представляет разобранное Karpik-решение и все входящие в него проекты.</summary>
/// <param name="SolutionPath">Нормализованный путь к решению.</param>
/// <param name="SdkVersion">Версия Karpik SDK, закреплённая решением.</param>
/// <param name="Projects">Проекты, объявленные в решении.</param>
public sealed record KarpikSolutionModel(
    string SolutionPath,
    string SdkVersion,
    IReadOnlyList<KarpikProjectDescriptor> Projects);
