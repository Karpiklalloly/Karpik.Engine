using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Network.Codegen;

#pragma warning disable RS2008
internal static class ProjectTypeDetector
{
    private const string SideProperty = "build_property.KarpikSide";
    private const string ProjectKindProperty = "build_property.KarpikProjectKind";
    private const string CompositionModeProperty = "build_property.KarpikCompositionMode";

    private static readonly DiagnosticDescriptor InvalidBuildProperties = new(
        "KNET001",
        "Invalid Karpik network generator build properties",
        "Network generation requires KarpikSide=Shared|Client|Server, KarpikProjectKind=Runtime|Tool|Test, and KarpikCompositionMode=Dynamic|Static; actual values were Side='{0}', ProjectKind='{1}', CompositionMode='{2}'",
        "Network.Codegen",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal enum ProjectType
    {
        Server,
        Client,
        Shared,
        Unknown,
    }

    internal readonly record struct ProjectProperties(
        ProjectType Side,
        string ProjectKind,
        string CompositionMode)
    {
        internal bool IsRuntime => string.Equals(ProjectKind, "Runtime", StringComparison.Ordinal);
    }

    internal static bool TryGetProjectProperties(
        AnalyzerConfigOptionsProvider optionsProvider,
        SourceProductionContext context,
        out ProjectProperties properties)
    {
        var options = optionsProvider.GlobalOptions;
        var hasSide = options.TryGetValue(SideProperty, out var side);
        var hasProjectKind = options.TryGetValue(ProjectKindProperty, out var projectKind);
        var hasCompositionMode = options.TryGetValue(CompositionModeProperty, out var compositionMode);

        if (!hasSide && !hasProjectKind && !hasCompositionMode)
        {
            properties = default;
            return false;
        }

        var projectType = side switch
        {
            "Server" => ProjectType.Server,
            "Client" => ProjectType.Client,
            "Shared" => ProjectType.Shared,
            _ => ProjectType.Unknown,
        };
        var validProjectKind = projectKind is "Runtime" or "Tool" or "Test";
        var validCompositionMode = compositionMode is "Dynamic" or "Static";
        if (projectType == ProjectType.Unknown || !validProjectKind || !validCompositionMode)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidBuildProperties,
                Location.None,
                side ?? string.Empty,
                projectKind ?? string.Empty,
                compositionMode ?? string.Empty));
            properties = default;
            return false;
        }

        properties = new ProjectProperties(projectType, projectKind!, compositionMode!);
        return true;
    }
}
#pragma warning restore RS2008
