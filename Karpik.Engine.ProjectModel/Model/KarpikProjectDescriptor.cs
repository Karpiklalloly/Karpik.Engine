namespace Karpik.Engine.ProjectModel;

public sealed record KarpikProjectDescriptor(
    string ProjectPath,
    IReadOnlyList<string> SdkNames,
    KarpikProjectKind Kind,
    KarpikProjectSide Side,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<KarpikModuleReference> Modules)
{
    internal KarpikProjectReadStatus ReadStatus { get; init; }
    internal bool ProjectReferencesAreStatic { get; init; }
}
