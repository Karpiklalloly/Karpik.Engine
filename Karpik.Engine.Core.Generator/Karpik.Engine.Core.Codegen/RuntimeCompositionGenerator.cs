using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Karpik.Engine.Core.Codegen;

#pragma warning disable RS2008
[Generator]
public sealed class RuntimeCompositionGenerator : IIncrementalGenerator
{
    private const string InstallerInterfaceName = "Karpik.Engine.Core.IModuleInstaller";
    private const string ModuleAttributeName = "Karpik.Engine.Core.ModuleAttribute";

    private const string SideProperty = "build_property.KarpikSide";
    private const string CompositionModeProperty = "build_property.KarpikCompositionMode";

    private static readonly DiagnosticDescriptor InvalidBuildProperties = new(
        "KCORE001",
        "Invalid Karpik runtime composition build properties",
        "Runtime composition generation requires KarpikSide=Client|Server|Shared and KarpikCompositionMode=Dynamic|Static; actual values were Side='{0}', CompositionMode='{1}'",
        "Karpik.Engine.Core.Codegen",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidModuleInstaller = new(
        "KCORE002",
        "Invalid module installer for static composition",
        "Module installer '{0}' must be a concrete public class with a public parameterless constructor to participate in static composition",
        "Karpik.Engine.Core.Codegen",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateModuleIdentity = new(
        "KCORE003",
        "Duplicate module identity",
        "Module installer '{0}' was discovered in more than one assembly ('{1}', '{2}'); static composition requires a unique module identity",
        "Karpik.Engine.Core.Codegen",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AmbiguousModuleImplementation = new(
        "KCORE004",
        "Ambiguous module implementation",
        "Module installers '{0}' and '{1}' register the same module contract through inheritance; select exactly one implementation",
        "Karpik.Engine.Core.Codegen",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var properties = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) => CompositionProperties.Read(provider.GlobalOptions));
        context.RegisterSourceOutput(
            properties.Combine(context.CompilationProvider),
            static (context, state) => Execute(context, state.Left, state.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        CompositionProperties properties,
        Compilation compilation)
    {
        if (!properties.AnyPresent)
        {
            return;
        }

        var validSide = properties.Side is "Client" or "Shared" or "Server";
        var validMode = properties.CompositionMode is "Dynamic" or "Static";
        if (!validSide || !validMode)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidBuildProperties,
                Location.None,
                properties.Side,
                properties.CompositionMode));
            return;
        }

        if (properties.CompositionMode != "Static" || properties.Side == "Shared")
        {
            return;
        }

        INamedTypeSymbol? installerInterface = compilation.GetTypeByMetadataName(InstallerInterfaceName);
        INamedTypeSymbol? moduleAttribute = compilation.GetTypeByMetadataName(ModuleAttributeName);
        if (installerInterface is null || moduleAttribute is null)
        {
            return;
        }

        List<ModuleInstaller> installers = CollectInstallers(compilation, installerInterface, moduleAttribute, context);
        ReportAmbiguousImplementations(installers, context);

        installers.Sort(static (left, right) =>
        {
            int comparison = left.Scope.CompareTo(right.Scope);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Priority.CompareTo(right.Priority);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = string.CompareOrdinal(left.AssemblyDisplayName, right.AssemblyDisplayName);
            return comparison != 0
                ? comparison
                : string.CompareOrdinal(left.FullName, right.FullName);
        });

        context.AddSource(
            "GeneratedRuntimeComposition.g.cs",
            SourceText.From(GenerateSource(installers), Encoding.UTF8));
    }

    private static List<ModuleInstaller> CollectInstallers(
        Compilation compilation,
        INamedTypeSymbol installerInterface,
        INamedTypeSymbol moduleAttribute,
        SourceProductionContext context)
    {
        var installers = new List<ModuleInstaller>();
        foreach (IAssemblySymbol assembly in EnumerateCandidateAssemblies(compilation, installerInterface))
        {
            foreach (INamedTypeSymbol type in GetTopLevelTypes(assembly.GlobalNamespace))
            {
                if (!IsAttributedInstaller(type, installerInterface, moduleAttribute))
                {
                    continue;
                }

                string fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty);
                if (!IsValidInstallerShape(type, fullName, context))
                {
                    continue;
                }

                AttributeData attribute = type.GetAttributes()[GetModuleAttributeIndex(type, moduleAttribute)];
                var installer = new ModuleInstaller(
                    type,
                    fullName,
                    Convert.ToInt32(attribute.ConstructorArguments[0].Value),
                    Convert.ToInt32(attribute.ConstructorArguments.Length > 1 ? attribute.ConstructorArguments[1].Value : 0),
                    assembly.Identity.GetDisplayName());
                installers.Add(installer);
            }
        }

        ReportDuplicates(installers, context);
        return installers;
    }

    private static IEnumerable<IAssemblySymbol> EnumerateCandidateAssemblies(
        Compilation compilation,
        INamedTypeSymbol installerInterface)
    {
        yield return compilation.Assembly;
        foreach (IAssemblySymbol reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (ReferencesAssembly(reference, installerInterface.ContainingAssembly.Identity))
            {
                yield return reference;
            }
        }
    }

    private static bool ReferencesAssembly(IAssemblySymbol assembly, AssemblyIdentity target)
    {
        foreach (IModuleSymbol module in assembly.Modules)
        {
            foreach (IAssemblySymbol referenced in module.ReferencedAssemblySymbols)
            {
                if (referenced.Identity.Equals(target))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetTopLevelTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (INamespaceOrTypeSymbol member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol nested)
            {
                foreach (INamedTypeSymbol type in GetTopLevelTypes(nested))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
            }
        }
    }

    private static bool IsAttributedInstaller(
        INamedTypeSymbol type,
        INamedTypeSymbol installerInterface,
        INamedTypeSymbol moduleAttribute)
    {
        if (type.TypeKind != TypeKind.Class)
        {
            return false;
        }

        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, installerInterface))
            {
                return GetModuleAttributeIndex(type, moduleAttribute) >= 0;
            }
        }

        return false;
    }

    private static int GetModuleAttributeIndex(INamedTypeSymbol type, INamedTypeSymbol moduleAttribute)
    {
        ImmutableArray<AttributeData> attributes = type.GetAttributes();
        for (int index = 0; index < attributes.Length; index++)
        {
            if (SymbolEqualityComparer.Default.Equals(attributes[index].AttributeClass, moduleAttribute))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsValidInstallerShape(INamedTypeSymbol type, string fullName, SourceProductionContext context)
    {
        if (type.IsAbstract
            || type.DeclaredAccessibility != Accessibility.Public
            || type.IsGenericType)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidModuleInstaller, GetLocation(type), fullName));
            return false;
        }

        foreach (IMethodSymbol constructor in type.Constructors)
        {
            if (!constructor.IsStatic
                && constructor.Parameters.Length == 0
                && constructor.DeclaredAccessibility == Accessibility.Public)
            {
                return true;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(InvalidModuleInstaller, GetLocation(type), fullName));
        return false;
    }

    private static void ReportDuplicates(List<ModuleInstaller> installers, SourceProductionContext context)
    {
        var unique = new Dictionary<string, ModuleInstaller>(StringComparer.Ordinal);
        var kept = new List<ModuleInstaller>(installers.Count);
        foreach (ModuleInstaller installer in installers)
        {
            if (unique.TryGetValue(installer.FullName, out ModuleInstaller? existing))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateModuleIdentity,
                    GetLocation(installer.Type),
                    installer.FullName,
                    existing.AssemblyDisplayName,
                    installer.AssemblyDisplayName));
                continue;
            }

            unique.Add(installer.FullName, installer);
            kept.Add(installer);
        }

        installers.Clear();
        installers.AddRange(kept);
    }

    private static void ReportAmbiguousImplementations(List<ModuleInstaller> installers, SourceProductionContext context)
    {
        for (int outer = 0; outer < installers.Count; outer++)
        {
            for (int inner = 0; inner < installers.Count; inner++)
            {
                if (outer == inner)
                {
                    continue;
                }

                if (DerivesFrom(installers[inner].Type, installers[outer].Type))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        AmbiguousModuleImplementation,
                        GetLocation(installers[inner].Type),
                        installers[inner].FullName,
                        installers[outer].FullName));
                }
            }
        }
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static Location GetLocation(INamedTypeSymbol type) =>
        type.Locations.Length > 0 ? type.Locations[0] : Location.None;

    private static string GenerateSource(List<ModuleInstaller> installers)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine();
        builder.AppendLine("namespace Karpik.Engine.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    internal sealed class GeneratedRuntimeComposition : global::Karpik.Engine.Core.IStaticRuntimeComposition");
        builder.AppendLine("    {");
        builder.AppendLine("        public void RegisterModules(global::Karpik.Engine.Core.IStaticModuleRegistry registry)");
        builder.AppendLine("        {");
        foreach (ModuleInstaller installer in installers)
        {
            builder.Append("            registry.Add(new global::");
            builder.Append(installer.FullName);
            builder.AppendLine("());");
        }
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void RegisterServices(global::Karpik.Engine.Core.IStaticServiceRegistry registry)");
        builder.AppendLine("        {");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private sealed class ModuleInstaller
    {
        internal ModuleInstaller(
            INamedTypeSymbol type,
            string fullName,
            int scope,
            int priority,
            string assemblyDisplayName)
        {
            Type = type;
            FullName = fullName;
            Scope = scope;
            Priority = priority;
            AssemblyDisplayName = assemblyDisplayName;
        }

        internal INamedTypeSymbol Type { get; }
        internal string FullName { get; }
        internal int Scope { get; }
        internal int Priority { get; }
        internal string AssemblyDisplayName { get; }
    }

    internal readonly struct CompositionProperties
    {
        internal CompositionProperties(bool anyPresent, string side, string compositionMode)
        {
            AnyPresent = anyPresent;
            Side = side;
            CompositionMode = compositionMode;
        }

        internal bool AnyPresent { get; }
        internal string Side { get; }
        internal string CompositionMode { get; }

        internal static CompositionProperties Read(AnalyzerConfigOptions options)
        {
            bool hasSide = options.TryGetValue(SideProperty, out string? side);
            bool hasMode = options.TryGetValue(CompositionModeProperty, out string? mode);
            return new CompositionProperties(hasSide || hasMode, side ?? string.Empty, mode ?? string.Empty);
        }
    }
}
#pragma warning restore RS2008
