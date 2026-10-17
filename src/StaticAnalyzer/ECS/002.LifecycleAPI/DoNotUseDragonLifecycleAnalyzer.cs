using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace StaticAnalyzer.ECS;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotUseDragonLifecycleAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.DoNotUseDragonLifecycleAPI,
        title: "Use Karpik lifecycle API instead of Dragon lifecycle API",
        messageFormat: "Use Karpik lifecycle interfaces instead of raw Dragon lifecycle interface '{0}'",
        category: "ECS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly ImmutableHashSet<string> ForbiddenLifecycleInterfaces = ImmutableHashSet.Create(
        "IEcsPreInit",
        "IEcsInit",
        "IEcsRun",
        "IEcsRunFinally",
        "IEcsDestroy",
        "IEcsRunLate",
        "IEcsRunParallel",
        "IEcsRunOnRequest",
        "IEcsProcess");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (IsBackendAssembly(context.Compilation.AssemblyName))
            return;

        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface))
            return;

        if (ImplementsAllowedEventSystem(type))
            return;

        foreach (var iface in type.AllInterfaces)
        {
            if (!ForbiddenLifecycleInterfaces.Contains(iface.Name))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                type.Locations[0],
                iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            return;
        }
    }

    private static bool IsBackendAssembly(string assemblyName)
    {
        return assemblyName is "ECS.Core";
    }

    private static bool ImplementsAllowedEventSystem(INamedTypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Name is "IEcsRunOnEvent")
                return true;
        }

        return false;
    }
}
