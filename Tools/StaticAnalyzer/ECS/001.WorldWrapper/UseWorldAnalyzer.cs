using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace StaticAnalyzer.ECS;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UseWorldAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.UseWorldInsteadOfEcsWorld,
        title: "Use wrapped ECS world in systems",
        messageFormat: "Use '{0}' instead of raw '{1}' in systems",
        category: "ECS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
    
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }
    
    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;

        if (!IsSystem(field.ContainingType))
            return;

        if (TryGetReplacement(field.Type, out var replacement))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                field.Locations[0],
                replacement,
                field.Type.ToDisplayString()));
        }
    }
    
    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;

        if (!IsSystem(property.ContainingType))
            return;

        if (TryGetReplacement(property.Type, out var replacement))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                property.Locations[0],
                replacement,
                property.Type.ToDisplayString()));
        }
    }
    
    internal static bool TryGetReplacement(ITypeSymbol type, out string replacement)
    {
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        replacement = name switch
        {
            "global::EcsDefaultWorld"
                or "global::Dragon.EcsDefaultWorld"
                or "global::DCFApixels.DragonECS.EcsDefaultWorld"
                => "Karpik.Engine.Shared.ECS.DefaultWorld",

            "global::EcsEventWorld"
                or "global::Dragon.EcsEventWorld"
                or "global::DCFApixels.DragonECS.EcsEventWorld"
                => "Karpik.Engine.Shared.ECS.EventWorld",

            "global::Karpik.Engine.Shared.ECS.EcsMetaWorld"
                => "Karpik.Engine.Shared.ECS.MetaWorld",

            "global::EcsWorld"
                or "global::Dragon.EcsWorld"
                or "global::DCFApixels.DragonECS.EcsWorld"
                => "DragonExtensions.World",

            _ => null!
        };

        return replacement is not null;
    }
    
    private static bool IsSystem(INamedTypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            var name = iface.Name;

            if (name is "ISystem" or "IEcsProcess")
            {
                return true;
            }
        }

        return false;
    }
}
