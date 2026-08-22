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

    private const string ServiceRegistrationAttributeName = "Karpik.Engine.Core.ServiceRegistrationAttribute";
    private const string ExportAttributeName = "System.Composition.ExportAttribute";
    private const string SystemInterfaceName = "Karpik.Engine.Core.ISystem";
    private const string StartableInterfaceName = "Autofac.IStartable";

    // Stable numeric values of the Karpik.Engine.Core contracts; this generator
    // project intentionally does not reference the runtime assembly.
    private const int SimulationScopeValue = 2;
    private const int TransientLifetimeValue = 1;

    private static readonly DiagnosticDescriptor MissingExportAttribute = new(
        "KE304",
        "Service registration without export",
        "Service '{0}' has [ServiceRegistration] but no [Export] contract; add at least one [Export] or remove [ServiceRegistration]",
        "Karpik.Engine.Core.Codegen",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidServiceImplementation = new(
        "KE305",
        "Invalid service implementation for static composition",
        "Service '{0}' must be a concrete class visible from the host assembly with an accessible constructor to participate in static composition",
        "Karpik.Engine.Core.Codegen",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AmbiguousServiceConstructor = new(
        "KE306",
        "Ambiguous service constructor",
        "Service '{0}' declares more than one accessible constructor; static composition requires exactly one",
        "Karpik.Engine.Core.Codegen",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnresolvableDependency = new(
        "KE307",
        "Definitely unresolvable constructor dependency",
        "Constructor parameter {1} of '{0}' has scalar type '{2}' which cannot be resolved through IServiceResolver; register it as a service or change the parameter type",
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

        INamedTypeSymbol? systemInterface = compilation.GetTypeByMetadataName(SystemInterfaceName);
        List<ServiceModel> registrations = CollectServiceRegistrations(
            compilation,
            installerInterface,
            context);

        if (systemInterface is not null)
        {
            registrations.AddRange(CollectSystemRegistrations(
                compilation,
                installerInterface,
                systemInterface,
                registrations,
                context));
        }

        context.AddSource(
            "GeneratedRuntimeComposition.g.cs",
            SourceText.From(GenerateSource(installers, registrations), Encoding.UTF8));
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
                    ReadPriority(attribute),
                    assembly.Identity.GetDisplayName());
                installers.Add(installer);
            }
        }

        ReportDuplicates(installers, context);
        return installers;
    }

    private static int ReadPriority(AttributeData attribute)
    {
        // Metadata-decoded constructor arguments include defaulted parameters,
        // so a named argument must be applied after them, mirroring runtime
        // semantics where the property setter runs after the constructor.
        int priority = attribute.ConstructorArguments.Length > 1
            ? Convert.ToInt32(attribute.ConstructorArguments[1].Value)
            : 0;

        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArguments = attribute.NamedArguments;
        for (int index = 0; index < namedArguments.Length; index++)
        {
            KeyValuePair<string, TypedConstant> named = namedArguments[index];
            if (named.Key == "Priority")
            {
                priority = Convert.ToInt32(named.Value.Value);
                break;
            }
        }

        return priority;
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

    private static List<ServiceModel> CollectServiceRegistrations(
        Compilation compilation,
        INamedTypeSymbol installerInterface,
        SourceProductionContext context)
    {
        var services = new List<ServiceModel>();
        INamedTypeSymbol? serviceAttribute = compilation.GetTypeByMetadataName(ServiceRegistrationAttributeName);
        INamedTypeSymbol? exportAttribute = compilation.GetTypeByMetadataName(ExportAttributeName);
        INamedTypeSymbol? startableInterface = compilation.GetTypeByMetadataName(StartableInterfaceName);
        if (serviceAttribute is null || exportAttribute is null)
        {
            return services;
        }

        foreach (IAssemblySymbol assembly in EnumerateCandidateAssemblies(compilation, installerInterface))
        {
            foreach (INamedTypeSymbol type in GetTopLevelTypes(assembly.GlobalNamespace))
            {
                AttributeData? registration = FindAttribute(type, serviceAttribute);
                if (registration is null || type.TypeKind != TypeKind.Class)
                {
                    continue;
                }

                string fullName = ToFullName(type);
                ImmutableArray<AttributeData> attributes = type.GetAttributes();
                int exportCount = 0;
                for (int index = 0; index < attributes.Length; index++)
                {
                    if (SymbolEqualityComparer.Default.Equals(attributes[index].AttributeClass, exportAttribute))
                    {
                        exportCount++;
                    }
                }

                if (exportCount == 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(MissingExportAttribute, GetLocation(type), fullName));
                    continue;
                }

                if (type.IsAbstract
                    || type.IsGenericType
                    || !IsAccessibleFromHost(type, compilation.Assembly))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidServiceImplementation, GetLocation(type), fullName));
                    continue;
                }

                List<IMethodSymbol> constructors = SelectVisibleConstructors(type, compilation.Assembly);
                if (constructors.Count == 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidServiceImplementation, GetLocation(type), fullName));
                    continue;
                }

                if (constructors.Count > 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(AmbiguousServiceConstructor, GetLocation(type), fullName));
                    continue;
                }

                IMethodSymbol constructor = constructors[0];
                bool unresolvable = false;
                for (int index = 0; index < constructor.Parameters.Length; index++)
                {
                    IParameterSymbol parameter = constructor.Parameters[index];
                    if (!IsDefinitelyUnresolvable(parameter))
                    {
                        continue;
                    }

                    unresolvable = true;
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnresolvableDependency,
                        GetLocation(type),
                        fullName,
                        index,
                        ToFullName(parameter.Type)));
                }

                services.Add(new ServiceModel(
                    type,
                    fullName,
                    assembly.Identity.GetDisplayName(),
                    ReadScope(registration),
                    ReadLifetime(registration),
                    CollectContracts(exportAttribute, startableInterface, attributes, type),
                    constructor,
                    unresolvable));
            }
        }

        services.Sort(static (left, right) =>
        {
            int comparison = left.Scope.CompareTo(right.Scope);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = string.CompareOrdinal(left.AssemblyDisplayName, right.AssemblyDisplayName);
            return comparison != 0
                ? comparison
                : string.CompareOrdinal(left.FullName, right.FullName);
        });
        return services;
    }

    private static IEnumerable<ServiceModel> CollectSystemRegistrations(
        Compilation compilation,
        INamedTypeSymbol installerInterface,
        INamedTypeSymbol systemInterface,
        List<ServiceModel> existingServices,
        SourceProductionContext context)
    {
        var serviceTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (ServiceModel service in existingServices)
        {
            serviceTypes.Add(service.Type);
        }

        var systems = new List<ServiceModel>();
        foreach (IAssemblySymbol assembly in EnumerateCandidateAssemblies(compilation, installerInterface))
        {
            foreach (INamedTypeSymbol type in GetTopLevelTypes(assembly.GlobalNamespace))
            {
                if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType)
                {
                    continue;
                }

                if (serviceTypes.Contains(type) || !ImplementsInterface(type, systemInterface))
                {
                    continue;
                }

                if (!IsAccessibleFromHost(type, compilation.Assembly))
                {
                    // Systems are discovered without an explicit marker attribute; types that could
                    // never be activated from generated code are skipped silently because modules
                    // may also provide them through metadata-invisible registrations.
                    continue;
                }

                List<IMethodSymbol> constructors = SelectVisibleConstructors(type, compilation.Assembly);
                if (constructors.Count == 0)
                {
                    continue;
                }

                if (constructors.Count > 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(AmbiguousServiceConstructor, GetLocation(type), ToFullName(type)));
                    continue;
                }

                IMethodSymbol constructor = constructors[0];
                for (int index = 0; index < constructor.Parameters.Length; index++)
                {
                    IParameterSymbol parameter = constructor.Parameters[index];
                    if (!IsDefinitelyUnresolvable(parameter))
                    {
                        continue;
                    }

                    context.ReportDiagnostic(Diagnostic.Create(
                        UnresolvableDependency,
                        GetLocation(type),
                        ToFullName(type),
                        index,
                        ToFullName(parameter.Type)));
                }

                systems.Add(new ServiceModel(
                    type,
                    ToFullName(type),
                    assembly.Identity.GetDisplayName(),
                    SimulationScopeValue,
                    TransientLifetimeValue,
                    [type],
                    constructor,
                    HasUnresolvableParameter(constructor)));
            }
        }

        systems.Sort(static (left, right) =>
        {
            int comparison = string.CompareOrdinal(left.AssemblyDisplayName, right.AssemblyDisplayName);
            return comparison != 0
                ? comparison
                : string.CompareOrdinal(left.FullName, right.FullName);
        });
        return systems;
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceSymbol)
    {
        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, interfaceSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAccessibleFromHost(INamedTypeSymbol type, IAssemblySymbol hostAssembly)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (!IsAccessibleFromHostSymbol(current.DeclaredAccessibility, current.ContainingAssembly, hostAssembly))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAccessibleFromHostSymbol(
        Accessibility accessibility,
        IAssemblySymbol declaringAssembly,
        IAssemblySymbol hostAssembly)
    {
        return SymbolEqualityComparer.Default.Equals(declaringAssembly, hostAssembly)
            ? accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
            : accessibility == Accessibility.Public;
    }

    private static List<IMethodSymbol> SelectVisibleConstructors(INamedTypeSymbol type, IAssemblySymbol hostAssembly)
    {
        var candidates = new List<IMethodSymbol>();
        foreach (IMethodSymbol candidate in type.Constructors)
        {
            if (candidate.IsStatic || !IsAccessibleFromHostSymbol(candidate.DeclaredAccessibility, candidate.ContainingAssembly, hostAssembly))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        return candidates;
    }

    private static bool IsDefinitelyUnresolvable(IParameterSymbol parameter)
    {
        if (parameter.RefKind != RefKind.None)
        {
            return true;
        }

        return IsDefinitelyUnresolvable(parameter.Type);
    }

    private static bool IsDefinitelyUnresolvable(ITypeSymbol type)
    {
        if (type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
        {
            return true;
        }

        if (type.SpecialType != SpecialType.None)
        {
            return true;
        }

        return type.TypeKind == TypeKind.Enum;
    }

    private static bool HasUnresolvableParameter(IMethodSymbol constructor)
    {
        for (int index = 0; index < constructor.Parameters.Length; index++)
        {
            if (IsDefinitelyUnresolvable(constructor.Parameters[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static AttributeData? FindAttribute(INamedTypeSymbol type, INamedTypeSymbol attributeClass)
    {
        ImmutableArray<AttributeData> attributes = type.GetAttributes();
        for (int index = 0; index < attributes.Length; index++)
        {
            if (SymbolEqualityComparer.Default.Equals(attributes[index].AttributeClass, attributeClass))
            {
                return attributes[index];
            }
        }

        return null;
    }

    private static int ReadScope(AttributeData registration)
    {
        int scope = registration.ConstructorArguments.Length > 0
            ? Convert.ToInt32(registration.ConstructorArguments[0].Value)
            : 0;

        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArguments = registration.NamedArguments;
        for (int index = 0; index < namedArguments.Length; index++)
        {
            KeyValuePair<string, TypedConstant> named = namedArguments[index];
            if (named.Key == "Scope")
            {
                scope = Convert.ToInt32(named.Value.Value);
                break;
            }
        }

        return scope;
    }

    private static int ReadLifetime(AttributeData registration)
    {
        int lifetime = registration.ConstructorArguments.Length > 1
            ? Convert.ToInt32(registration.ConstructorArguments[1].Value)
            : 0;

        ImmutableArray<KeyValuePair<string, TypedConstant>> namedArguments = registration.NamedArguments;
        for (int index = 0; index < namedArguments.Length; index++)
        {
            KeyValuePair<string, TypedConstant> named = namedArguments[index];
            if (named.Key == "Lifetime")
            {
                lifetime = Convert.ToInt32(named.Value.Value);
                break;
            }
        }

        return lifetime;
    }

    private static List<INamedTypeSymbol> CollectContracts(
        INamedTypeSymbol exportAttribute,
        INamedTypeSymbol? startableInterface,
        ImmutableArray<AttributeData> attributes,
        INamedTypeSymbol implementation)
    {
        var contracts = new List<INamedTypeSymbol>();
        for (int index = 0; index < attributes.Length; index++)
        {
            AttributeData attribute = attributes[index];
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, exportAttribute))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is INamedTypeSymbol contract)
            {
                contracts.Add(contract);
            }
            else
            {
                contracts.Add(implementation);
            }
        }

        contracts.Sort(static (left, right) => string.CompareOrdinal(ToFullName(left), ToFullName(right)));

        if (startableInterface is not null && ImplementsInterface(implementation, startableInterface))
        {
            contracts.Add(startableInterface);
        }

        // Deduplicate identical contracts while keeping deterministic ordering.
        var unique = new List<INamedTypeSymbol>(contracts.Count);
        for (int index = 0; index < contracts.Count; index++)
        {
            if (index == 0 || !SymbolEqualityComparer.Default.Equals(contracts[index], contracts[index - 1]))
            {
                unique.Add(contracts[index]);
            }
        }

        return unique;
    }

    private static string ToFullName(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);

    private static string GenerateSource(List<ModuleInstaller> installers, List<ServiceModel> registrations)
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
        foreach (ServiceModel model in registrations)
        {
            string factory = BuildFactory(model);
            foreach (INamedTypeSymbol contract in model.Contracts)
            {
                builder.Append("            registry.Register<global::").Append(ToFullName(contract));
                builder.Append(", global::").Append(model.FullName).Append('>');
                builder.Append("(global::Karpik.Engine.Core.ModuleScope.").Append(ScopeName(model.Scope));
                builder.Append(", global::Karpik.Engine.Core.ServiceLifetime.").Append(LifetimeName(model.Lifetime));
                builder.Append(", ").Append(factory).AppendLine(");");
            }
        }
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildFactory(ServiceModel model)
    {
        var factory = new StringBuilder();
        factory.Append("static resolver => new global::").Append(model.FullName).Append('(');
        ImmutableArray<IParameterSymbol> parameters = model.Constructor.Parameters;
        for (int index = 0; index < parameters.Length; index++)
        {
            if (index > 0)
            {
                factory.Append(", ");
            }

            factory.Append(BuildArgument(parameters[index]));
        }

        factory.Append(')');
        return factory.ToString();
    }

    private static string BuildArgument(IParameterSymbol parameter)
    {
        ITypeSymbol type = parameter.Type;
        if (type is IArrayTypeSymbol arrayType)
        {
            // Autofac resolves element arrays natively; IServiceResolver exposes
            // the same capability through the non-generic Resolve(Type) overload.
            return $"(global::{ToFullName(type)})resolver.Resolve(typeof(global::{ToFullName(arrayType.ElementType)}[]))";
        }

        return $"resolver.Resolve<global::{ToFullName(type)}>()";
    }

    private static string ScopeName(int scope) => scope switch
    {
        0 => "Engine",
        1 => "ModSet",
        2 => "Simulation",
        _ => scope.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static string LifetimeName(int lifetime) => lifetime switch
    {
        0 => "Singleton",
        1 => "Transient",
        _ => lifetime.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

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

    private sealed class ServiceModel
    {
        internal ServiceModel(
            INamedTypeSymbol type,
            string fullName,
            string assemblyDisplayName,
            int scope,
            int lifetime,
            List<INamedTypeSymbol> contracts,
            IMethodSymbol constructor,
            bool hasUnresolvableParameter)
        {
            Type = type;
            FullName = fullName;
            AssemblyDisplayName = assemblyDisplayName;
            Scope = scope;
            Lifetime = lifetime;
            Contracts = contracts;
            Constructor = constructor;
            HasUnresolvableParameter = hasUnresolvableParameter;
        }

        internal INamedTypeSymbol Type { get; }
        internal string FullName { get; }
        internal string AssemblyDisplayName { get; }
        internal int Scope { get; }
        internal int Lifetime { get; }
        internal List<INamedTypeSymbol> Contracts { get; }
        internal IMethodSymbol Constructor { get; }
        internal bool HasUnresolvableParameter { get; }
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
