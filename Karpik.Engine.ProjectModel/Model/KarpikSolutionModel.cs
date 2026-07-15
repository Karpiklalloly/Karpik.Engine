namespace Karpik.Engine.ProjectModel;

public sealed record KarpikSolutionModel(
    string SolutionPath,
    string SdkVersion,
    IReadOnlyList<KarpikProjectDescriptor> Projects);
