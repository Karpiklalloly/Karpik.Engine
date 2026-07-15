namespace Karpik.Engine.ProjectModel;

public sealed record KarpikDiagnostic(
    string Code,
    string ProjectPath,
    string Message,
    KarpikDiagnosticReason Reason = KarpikDiagnosticReason.None);
