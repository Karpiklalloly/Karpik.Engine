namespace Karpik.Engine.ProjectModel;

public static class KarpikDiagnosticCodes
{
    public const string MissingSdk = "KARPIK001";
    public const string InvalidProjectKind = "KARPIK002";
    public const string InvalidProjectSide = "KARPIK003";
    public const string InvalidSolutionProject = "KARPIK004";
    public const string ForbiddenSideDependency = "KARPIK005";
    public const string ProjectReferenceCycle = "KARPIK006";
    public const string UnknownOrAmbiguousModule = "KARPIK007";
    public const string MissingRequiredModule = "KARPIK008";
}
