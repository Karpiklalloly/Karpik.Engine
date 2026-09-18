using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    private const string ProjectKindProperty = "build_property.KarpikProjectKind";
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
    private const string SystemUpdateInterfaceName = "Karpik.Engine.Core.ISystemUpdate";
    private const string SystemRenderPrepareInterfaceName = "Karpik.Engine.Core.ISystemRenderPrepare";

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
        // Incremental pipeline: candidate discovery for the compilation's OWN
        // sources runs through syntax/symbol providers whose results Roslyn
        // caches per declaration, so editing one file never rescans unrelated
        // code. Only the referenced-assembly scan still needs Compilation; it
        // is memoized behind an assembly-identity + referenced-source-content
        // key so unchanged references cost one dictionary lookup per edit
        // instead of a full symbol walk.
        var properties = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) => CompositionProperties.Read(provider.GlobalOptions));

        var ownInstallers = context.SyntaxProvider.ForAttributeWithMetadataName(
            ModuleAttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => AnalyzeInstallerCandidate(ctx));

        var ownServices = context.SyntaxProvider.ForAttributeWithMetadataName(
            ServiceRegistrationAttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => AnalyzeServiceCandidate(ctx));

        var ownSystems = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => IsSystemLikeDeclaration(node),
            static (ctx, _) => AnalyzeSystemCandidate(ctx));

        var ownComponents = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => IsComponentLikeDeclaration(node),
            static (ctx, _) => AnalyzeComponentCandidate(ctx));

        var referenced = context.CompilationProvider.Select(static (compilation, _) =>
            ReferencedScan.Scan(compilation));

        var own = ownInstallers
            .Collect()
            .Combine(ownServices.Collect())
            .Combine(ownSystems.Collect())
            .Combine(ownComponents.Collect());

        context.RegisterSourceOutput(
            properties.Combine(own.Combine(referenced)),
            static (context, state) => Execute(
                context,
                state.Left,
                state.Right.Left.Left.Left.Left,
                state.Right.Left.Left.Left.Right,
                state.Right.Left.Left.Right,
                state.Right.Left.Right,
                state.Right.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        CompositionProperties properties,
        ImmutableArray<InstallerModel> ownInstallers,
        ImmutableArray<ServiceModel> ownServices,
        ImmutableArray<SystemModel> ownSystems,
        ImmutableArray<ComponentRootModel> ownComponents,
        ReferencedModels referenced)
    {
        if (!properties.AnyPresent)
        {
            return;
        }

        if (properties.ProjectKind == "Test")
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

        List<InstallerModel> installers = MergeInstallers(ownInstallers, referenced.Installers, context);
        installers.Sort(InstallerOrder);

        List<ServiceModel> services = MergeServices(ownServices, referenced.Services);
        services.Sort(ServiceOrder);
        List<SystemModel> systemModels = MergeSystems(ownSystems, referenced.Systems, services);
        systemModels.Sort(static (left, right) => SystemOrder(left.Model!, right.Model!));
        List<ServiceModel> systems = systemModels
            .Select(static model => model.Model!)
            .ToList();

        List<ServiceModel> registrations = [.. services, .. systems];
        // Unified canonical emission order over BOTH services and systems
        // (scope asc -> assembly identity -> full name). Emitting all services
        // before all systems diverges from the Dynamic discovery sequence as
        // soon as more than one module assembly contributes Simulation-scope
        // registrations (second Milestone 9 audit).
        registrations.Sort(ServiceOrder);

        foreach (ServiceModel registration in registrations)
        {
            registration.ReportDiagnostics(context);
        }

        List<string> aotComponentTemplateRoots = ownComponents
            .Concat(referenced.Components)
            .Where(static component => component.RootExpression.Length > 0)
            .Select(static component => component.RootExpression)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static root => root, StringComparer.Ordinal)
            .ToList();

        List<SchedulingModel> updateSchedulingSystems = systemModels
            .Where(static system => system.IsUpdateSystem)
            .Select(static system => system.Scheduling)
            .ToList();
        List<SchedulingModel> renderPrepareSchedulingSystems = systemModels
            .Where(static system => system.IsRenderPrepareSystem)
            .Select(static system => system.Scheduling)
            .ToList();

        context.AddSource(
            "GeneratedRuntimeComposition.g.cs",
            SourceText.From(GenerateSource(
                installers,
                registrations,
                aotComponentTemplateRoots,
                updateSchedulingSystems,
                renderPrepareSchedulingSystems),
                Encoding.UTF8));
    }

    private static int InstallerOrder(InstallerModel left, InstallerModel right)
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
    }

    private static int ServiceOrder(ServiceModel left, ServiceModel right)
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
    }

    private static int SystemOrder(ServiceModel left, ServiceModel right)
    {
        int comparison = string.CompareOrdinal(left.AssemblyDisplayName, right.AssemblyDisplayName);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.FullName, right.FullName);
    }

    private static List<InstallerModel> MergeInstallers(
        ImmutableArray<InstallerModel> own,
        ImmutableArray<InstallerModel> referenced,
        SourceProductionContext context)
    {
        var merged = new List<InstallerModel>(own.Length + referenced.Length);
        var unique = new Dictionary<string, InstallerModel>(StringComparer.Ordinal);
        foreach (InstallerModel installer in own.Concat(referenced))
        {
            if (unique.TryGetValue(installer.FullName, out InstallerModel? existing))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateModuleIdentity,
                    installer.Location ?? Location.None,
                    installer.FullName,
                    existing.AssemblyDisplayName,
                    installer.AssemblyDisplayName));
                continue;
            }

            unique.Add(installer.FullName, installer);
            merged.Add(installer);
        }

        foreach (InstallerModel installer in merged)
        {
            foreach (InstallerModel other in merged)
            {
                if (!ReferenceEquals(installer, other) && installer.DerivesFrom(other))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        AmbiguousModuleImplementation,
                        installer.Location ?? Location.None,
                        installer.FullName,
                        other.FullName));
                }
            }
        }

        foreach (InstallerModel installer in merged)
        {
            installer.ReportTo(context);
        }

        return merged;
    }

    private static List<ServiceModel> MergeServices(
        ImmutableArray<ServiceModel> own,
        ImmutableArray<ServiceModel> referenced)
    {
        var merged = new List<ServiceModel>(own.Length + referenced.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ServiceModel service in own.Concat(referenced))
        {
            if (seen.Add(service.FullName))
            {
                merged.Add(service);
            }
        }

        return merged;
    }

    private static List<SystemModel> MergeSystems(
        ImmutableArray<SystemModel> own,
        ImmutableArray<SystemModel> referenced,
        List<ServiceModel> services)
    {
        var serviceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ServiceModel service in services)
        {
            serviceNames.Add(service.FullName);
        }

        var merged = new List<SystemModel>(own.Length + referenced.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SystemModel system in own.Concat(referenced))
        {
            if (system.Model is null || serviceNames.Contains(system.Model.FullName) || !seen.Add(system.Model.FullName))
            {
                continue;
            }

            merged.Add(system);
        }

        return merged;
    }

    // ---------------------------------------------------------------------
    // Own-assembly candidate analysis (syntax/symbol provider transforms)
    // ---------------------------------------------------------------------

    private static InstallerModel AnalyzeInstallerCandidate(GeneratorAttributeSyntaxContext ctx)
    {
        Location? location = GetSymbolLocation(ctx.TargetSymbol);
        if (ctx.TargetSymbol is not INamedTypeSymbol type
            || type.TypeKind != TypeKind.Class
            || type.ContainingType is not null)
        {
            return InstallerModel.Empty(location);
        }

        return AnalyzeInstaller(type, ctx.Attributes[0], AssemblyDisplayName(type), location);
    }

    private static ServiceModel AnalyzeServiceCandidate(GeneratorAttributeSyntaxContext ctx)
    {
        Location? location = GetSymbolLocation(ctx.TargetSymbol);
        if (ctx.TargetSymbol is not INamedTypeSymbol type
            || type.TypeKind != TypeKind.Class
            || type.ContainingType is not null)
        {
            return ServiceModel.Empty(location);
        }

        INamedTypeSymbol? exportAttribute =
            ctx.SemanticModel.Compilation.GetTypeByMetadataName(ExportAttributeName);
        INamedTypeSymbol? startableInterface =
            ctx.SemanticModel.Compilation.GetTypeByMetadataName(StartableInterfaceName);
        if (exportAttribute is null)
        {
            // Without the export-contract attribute type no service can ever be
            // analyzed; mirror the legacy behaviour of emitting nothing.
            return ServiceModel.Empty(location);
        }

        return AnalyzeService(
            type,
            ctx.Attributes[0],
            exportAttribute,
            startableInterface,
            ctx.SemanticModel.Compilation.Assembly,
            AssemblyDisplayName(type),
            location);
    }

    private static bool IsSystemLikeDeclaration(SyntaxNode node) =>
        node is ClassDeclarationSyntax { BaseList: not null } declaration
        && declaration.BaseList.Types.Any(static baseType =>
            baseType.Type is SimpleNameSyntax simple
            && simple.Identifier.Text.StartsWith("ISystem", StringComparison.Ordinal));

    private static SystemModel AnalyzeSystemCandidate(GeneratorSyntaxContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.SemanticModel.GetDeclaredSymbol(ctx.Node)!;
        Location? location = GetSymbolLocation(type);
        Compilation compilation = ctx.SemanticModel.Compilation;
        INamedTypeSymbol? systemInterface = compilation.GetTypeByMetadataName(SystemInterfaceName);
        if (type.ContainingType is not null
            || systemInterface is null
            || !ImplementsInterface(type, systemInterface))
        {
            return SystemModel.Empty;
        }

        INamedTypeSymbol? updateInterface = compilation.GetTypeByMetadataName(SystemUpdateInterfaceName);
        INamedTypeSymbol? renderPrepareInterface = compilation.GetTypeByMetadataName(SystemRenderPrepareInterfaceName);
        bool isUpdate = updateInterface is not null && ImplementsInterface(type, updateInterface);
        bool isRenderPrepare = renderPrepareInterface is not null && ImplementsInterface(type, renderPrepareInterface);
        ServiceModel? model = AnalyzeSystem(
            type,
            compilation.Assembly,
            AssemblyDisplayName(type),
            location);
        return new SystemModel(model, isUpdate, isRenderPrepare, ExtractScheduling(type));
    }

    private static bool IsComponentLikeDeclaration(SyntaxNode node) =>
        node is StructDeclarationSyntax { BaseList: not null } declaration
        && declaration.BaseList.Types.Any(static baseType =>
            baseType.Type is SimpleNameSyntax simple
            && (simple.Identifier.Text.StartsWith("IEcsComponent", StringComparison.Ordinal)
                || simple.Identifier.Text.StartsWith("IEcsTagComponent", StringComparison.Ordinal)));

    private static ComponentRootModel AnalyzeComponentCandidate(GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not INamedTypeSymbol type
            || type.ContainingType is not null)
        {
            return ComponentRootModel.Empty;
        }

        string? root = CollectComponentRoot(type, ctx.SemanticModel.Compilation);
        return root is null ? ComponentRootModel.Empty : new ComponentRootModel(root);
    }

    // ---------------------------------------------------------------------
    // Referenced-assembly scan (memoized by reference semantic identity)
    // ---------------------------------------------------------------------

    private static readonly object ReferencedCacheGate = new();

    private static readonly ConcurrentDictionary<string, ReferencedModels> ReferencedCache = new(StringComparer.Ordinal);

    // The referenced scan stays a memoized FULL scan keyed by content identity
    // (assembly identities + source fingerprints + PE MVIDs; RS1035-documented
    // deviation). To keep generator memory bounded across long IDE sessions,
    // the cache holds at most this many distinct keys and evicts the OLDEST
    // insertion first. Eviction is only a memory bound: an evicted key simply
    // recomputes its scan on the next generation.
    private const int MaxReferencedCacheEntries = 8;

    private static readonly List<string> ReferencedCacheInsertionOrder = new();

    private static readonly ConditionalWeakTable<Compilation, CompilationCacheIdentity>
        ReferencedCompilationIdentities = new();

    private static long _nextReferencedCompilationIdentity;

    private sealed class CompilationCacheIdentity
    {
        internal long Value { get; } = Interlocked.Increment(ref _nextReferencedCompilationIdentity);
    }

    private sealed class ReferencedScan
    {
        internal static ReferencedModels Scan(Compilation compilation)
        {
            var identityParts = new List<string>();
            foreach (IAssemblySymbol reference in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                identityParts.Add(reference.Identity.GetDisplayName());
            }

            // Assembly identities do not change when a referenced project's
            // sources are edited, so an identity-only key would serve stale
            // models for later host compilations in the same IDE session.
            // Fingerprint every reference by its complete immutable semantic
            // snapshot (CompilationReference) or module MVID (PE reference).
            AppendReferenceFingerprints(compilation, identityParts);

            string key = string.Join("|", identityParts);
            lock (ReferencedCacheGate)
            {
                if (!ReferencedCache.TryGetValue(key, out ReferencedModels cached))
                {
                    cached = ScanCore(compilation);
                    ReferencedCache[key] = cached;
                    ReferencedCacheInsertionOrder.Add(key);
                    while (ReferencedCache.Count > MaxReferencedCacheEntries
                           && ReferencedCacheInsertionOrder.Count > 0)
                    {
                        string oldest = ReferencedCacheInsertionOrder[0];
                        ReferencedCacheInsertionOrder.RemoveAt(0);
                        ReferencedCache.TryRemove(oldest, out _);
                    }
                }

                return cached;
            }
        }

        // RS1035 forbids file IO in analyzers because unbounded IO breaks
        // determinism. This is a deliberate, bounded exception required by the
        // Milestone 9 audit: referenced module binaries feed the referenced-scan
        // cache key so a rebuilt DLL invalidates stale models. The PRIMARY key
        // part is the module MVID read through Roslyn's own metadata snapshot
        // (PortableExecutableReference.GetMetadata) - content-exact identity
        // that survives rebuilds which restore timestamps/lengths, needs no
        // extra filesystem stats and cannot straddle a swap between the
        // compiler's metadata load and a stat. Stat values remain only as a
        // FALLBACK when the reference carries no loadable metadata.
        #pragma warning disable RS1035
        private static void AppendReferenceFingerprints(Compilation compilation, List<string> parts)
        {
            foreach (MetadataReference reference in compilation.References)
            {
                if (reference is CompilationReference compilationReference)
                {
                    // Roslyn Compilation is immutable. Its object identity
                    // therefore represents the complete semantic snapshot:
                    // syntax/parse options, compilation options and transitive
                    // references. Text hashes alone miss changes such as a new
                    // preprocessor symbol with byte-identical source files.
                    parts.Add("src:");
                    parts.Add(ReferencedCompilationIdentities
                        .GetValue(compilationReference.Compilation, static _ => new CompilationCacheIdentity())
                        .Value
                        .ToString(System.Globalization.CultureInfo.InvariantCulture));

                    continue;
                }

                // Production hosts reference modules as
                // PortableExecutableReference. A rebuilt DLL keeps its assembly
                // identity, so fingerprint the binary itself. The primary part
                // is the MVID from the metadata snapshot the reference already
                // loaded - content-exact, so two different binaries can never
                // share a cache key even when their stat values coincide. Only
                // if that metadata cannot be obtained do we degrade to a single-
                // FileInfo stat snapshot; generation must never crash.
                if (reference is PortableExecutableReference peReference
                    && peReference.FilePath is { Length: > 0 } pePath)
                {
                    string? mvidKey = TryGetModuleVersionIdKey(peReference);
                    if (mvidKey is not null)
                    {
                        parts.Add("pe-mvid:");
                        parts.Add(pePath);
                        parts.Add(mvidKey);
                        continue;
                    }

                    var peInfo = new FileInfo(pePath);
                    if (!peInfo.Exists)
                    {
                        // Explicit marker: a missing file must contribute key
                        // parts distinct from a reference that contributes none,
                        // otherwise both collapse to the same cache entry.
                        parts.Add("pe-missing:");
                        parts.Add(pePath);
                        continue;
                    }

                    parts.Add("pe:");
                    parts.Add(pePath);
                    parts.Add(peInfo.LastWriteTimeUtc.Ticks
                        .ToString(System.Globalization.CultureInfo.InvariantCulture));
                    parts.Add(peInfo.Length
                        .ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
        // Reads the MVID from the metadata the reference ALREADY carries. The
        // returned Metadata instance is owned and cached by the reference - it
        // must NOT be disposed here. Any failure (missing file, unreadable
        // image) degrades to the stat-based fallback instead of crashing
        // generation.
        private static string? TryGetModuleVersionIdKey(PortableExecutableReference reference)
        {
            try
            {
                return reference.GetMetadata() switch
                {
                    AssemblyMetadata assemblyMetadata => FormatAssemblyMvid(assemblyMetadata),
                    ModuleMetadata moduleMetadata => moduleMetadata.GetModuleVersionId().ToString("N"),
                    _ => null
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string FormatAssemblyMvid(AssemblyMetadata assemblyMetadata)
        {
            System.Collections.Immutable.ImmutableArray<ModuleMetadata> modules = assemblyMetadata.GetModules();
            if (modules.Length == 1)
            {
                return modules[0].GetModuleVersionId().ToString("N");
            }

            var builder = new System.Text.StringBuilder();
            foreach (ModuleMetadata module in modules)
            {
                builder.Append(module.GetModuleVersionId().ToString("N")).Append(';');
            }

            return builder.ToString();
        }
        #pragma warning restore RS1035

        private static ReferencedModels ScanCore(Compilation compilation)
        {
            INamedTypeSymbol? installerInterface = compilation.GetTypeByMetadataName(InstallerInterfaceName);
            INamedTypeSymbol? moduleAttribute = compilation.GetTypeByMetadataName(ModuleAttributeName);
            INamedTypeSymbol? serviceAttribute = compilation.GetTypeByMetadataName(ServiceRegistrationAttributeName);
            INamedTypeSymbol? exportAttribute = compilation.GetTypeByMetadataName(ExportAttributeName);
            INamedTypeSymbol? startableInterface = compilation.GetTypeByMetadataName(StartableInterfaceName);
            INamedTypeSymbol? systemInterface = compilation.GetTypeByMetadataName(SystemInterfaceName);
            INamedTypeSymbol? updateInterface = compilation.GetTypeByMetadataName(SystemUpdateInterfaceName);
            INamedTypeSymbol? renderPrepareInterface = compilation.GetTypeByMetadataName(SystemRenderPrepareInterfaceName);

            var installers = ImmutableArray.CreateBuilder<InstallerModel>();
            var services = ImmutableArray.CreateBuilder<ServiceModel>();
            var systems = ImmutableArray.CreateBuilder<SystemModel>();
            var components = ImmutableArray.CreateBuilder<ComponentRootModel>();

            foreach (IAssemblySymbol assembly in EnumerateReferencedAssemblies(compilation, installerInterface))
            {
                string assemblyDisplayName = assembly.Identity.GetDisplayName();
                foreach (INamedTypeSymbol type in GetTopLevelTypes(assembly.GlobalNamespace))
                {
                    AttributeData? moduleAttributeData = moduleAttribute is null
                        ? null
                        : FindAttribute(type, moduleAttribute);
                    if (moduleAttributeData is not null
                        && type.TypeKind == TypeKind.Class
                        && ImplementsInterface(type, installerInterface!))
                    {
                        installers.Add(AnalyzeInstaller(type, moduleAttributeData, assemblyDisplayName, GetSymbolLocation(type)));
                    }

                    AttributeData? registration = serviceAttribute is null
                        ? null
                        : FindAttribute(type, serviceAttribute);
                    if (registration is not null
                        && type.TypeKind == TypeKind.Class
                        && exportAttribute is not null)
                    {
                        services.Add(AnalyzeService(
                            type,
                            registration,
                            exportAttribute,
                            startableInterface,
                            compilation.Assembly,
                            assemblyDisplayName,
                            GetSymbolLocation(type)));
                    }

                    if (systemInterface is not null
                        && type.TypeKind == TypeKind.Class
                        && !type.IsAbstract
                        && !type.IsGenericType
                        && ImplementsInterface(type, systemInterface))
                    {
                        bool isUpdate = updateInterface is not null && ImplementsInterface(type, updateInterface);
                        bool isRenderPrepare = renderPrepareInterface is not null && ImplementsInterface(type, renderPrepareInterface);
                        systems.Add(new SystemModel(
                            AnalyzeSystem(type, compilation.Assembly, assemblyDisplayName, GetSymbolLocation(type)),
                            isUpdate,
                            isRenderPrepare,
                            ExtractScheduling(type)));
                    }

                    string? componentRoot = CollectComponentRoot(type, compilation);
                    if (componentRoot is not null)
                    {
                        components.Add(new ComponentRootModel(componentRoot));
                    }
                }
            }

            return new ReferencedModels(
                installers.ToImmutable(),
                services.ToImmutable(),
                systems.ToImmutable(),
                components.ToImmutable());
        }

        private static IEnumerable<IAssemblySymbol> EnumerateReferencedAssemblies(
            Compilation compilation,
            INamedTypeSymbol? installerInterface)
        {
            foreach (IAssemblySymbol reference in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                if (installerInterface is null
                    || reference.Identity.Equals(installerInterface.ContainingAssembly.Identity)
                    || ReferencesAssembly(reference, installerInterface.ContainingAssembly.Identity))
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
    }

    // ---------------------------------------------------------------------
    // Per-symbol analysis shared by both pipelines
    // ---------------------------------------------------------------------

    private static string AssemblyDisplayName(INamedTypeSymbol type) =>
        type.ContainingAssembly?.Identity.GetDisplayName() ?? string.Empty;

    private static Location? GetSymbolLocation(ISymbol symbol) =>
        symbol.Locations.Length > 0 ? symbol.Locations[0] : null;

    private static InstallerModel AnalyzeInstaller(
        INamedTypeSymbol type,
        AttributeData attribute,
        string assemblyDisplayName,
        Location? location)
    {
        string fullName = ToFullName(type);
        var baseChain = new List<string>();
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            baseChain.Add(ToFullName(current));
        }

        var diagnostics = new List<DiagnosticSpec>();
        bool valid = true;
        if (type.IsAbstract || type.DeclaredAccessibility != Accessibility.Public || type.IsGenericType)
        {
            diagnostics.Add(DiagnosticSpec.Create(InvalidModuleInstaller, fullName));
            valid = false;
        }
        else if (!HasPublicParameterlessConstructor(type))
        {
            diagnostics.Add(DiagnosticSpec.Create(InvalidModuleInstaller, fullName));
            valid = false;
        }

        return new InstallerModel(
            fullName,
            Convert.ToInt32(attribute.ConstructorArguments[0].Value),
            ReadPriority(attribute),
            assemblyDisplayName,
            baseChain.ToArray(),
            valid,
            diagnostics.ToArray(),
            location);
    }

    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol type)
    {
        foreach (IMethodSymbol constructor in type.Constructors)
        {
            if (!constructor.IsStatic
                && constructor.Parameters.Length == 0
                && constructor.DeclaredAccessibility == Accessibility.Public)
            {
                return true;
            }
        }

        return false;
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

    private static ServiceModel AnalyzeService(
        INamedTypeSymbol type,
        AttributeData registration,
        INamedTypeSymbol exportAttribute,
        INamedTypeSymbol? startableInterface,
        IAssemblySymbol hostAssembly,
        string assemblyDisplayName,
        Location? location)
    {
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
            return ServiceModel.Invalid(
                location,
                DiagnosticSpec.Create(MissingExportAttribute, fullName));
        }

        if (type.IsAbstract || type.IsGenericType || !IsAccessibleFromHost(type, hostAssembly))
        {
            return ServiceModel.Invalid(
                location,
                DiagnosticSpec.Create(InvalidServiceImplementation, fullName));
        }

        ConstructorAnalysis constructor = AnalyzeConstructors(type, hostAssembly, reportAccessibility: true, location);
        if (constructor.Diagnostic is not null)
        {
            return ServiceModel.Invalid(location, constructor.Diagnostic);
        }

        List<ParameterSpec> parameters = constructor.Parameters;
        var diagnostics = new List<DiagnosticSpec>();
        for (int index = 0; index < parameters.Count; index++)
        {
            if (parameters[index].IsDefinitelyUnresolvable)
            {
                diagnostics.Add(DiagnosticSpec.Create(
                    UnresolvableDependency,
                    fullName,
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    parameters[index].TypeFullName));
            }
        }

        return new ServiceModel(
            fullName,
            assemblyDisplayName,
            ReadScope(registration),
            ReadLifetime(registration),
            CollectContractNames(exportAttribute, startableInterface, attributes, type),
            parameters.ToArray(),
            false,
            true,
            diagnostics.ToArray(),
            location);
    }

    private static ServiceModel? AnalyzeSystem(
        INamedTypeSymbol type,
        IAssemblySymbol hostAssembly,
        string assemblyDisplayName,
        Location? location)
    {
        string fullName = ToFullName(type);
        if (type.IsAbstract || type.IsGenericType || !IsAccessibleFromHost(type, hostAssembly))
        {
            // Systems are discovered without an explicit marker attribute; types
            // that could never be activated from generated code are skipped
            // silently because modules may also provide them through
            // metadata-invisible registrations.
            return null;
        }

        ConstructorAnalysis constructor = AnalyzeConstructors(type, hostAssembly, reportAccessibility: false, location);
        if (constructor.Diagnostic is not null || !constructor.HasAccessibleConstructor)
        {
            return constructor.Diagnostic is not null
                ? ServiceModel.Invalid(location, constructor.Diagnostic)
                : null;
        }

        List<ParameterSpec> parameters = constructor.Parameters;
        var diagnostics = new List<DiagnosticSpec>();
        for (int index = 0; index < parameters.Count; index++)
        {
            if (parameters[index].IsDefinitelyUnresolvable)
            {
                diagnostics.Add(DiagnosticSpec.Create(
                    UnresolvableDependency,
                    fullName,
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    parameters[index].TypeFullName));
            }
        }

        return new ServiceModel(
            fullName,
            assemblyDisplayName,
            SimulationScopeValue,
            TransientLifetimeValue,
            [fullName],
            parameters.ToArray(),
            false,
            true,
            diagnostics.ToArray(),
            location);
    }

    private sealed class ConstructorAnalysis
    {
        internal List<ParameterSpec> Parameters { get; } = [];

        internal bool HasAccessibleConstructor { get; set; }

        internal DiagnosticSpec? Diagnostic { get; set; }
    }

    private static ConstructorAnalysis AnalyzeConstructors(
        INamedTypeSymbol type,
        IAssemblySymbol hostAssembly,
        bool reportAccessibility,
        Location? location)
    {
        var result = new ConstructorAnalysis();
        List<IMethodSymbol> candidates = SelectVisibleConstructors(type, hostAssembly);
        if (candidates.Count == 0)
        {
            if (reportAccessibility)
            {
                result.Diagnostic = DiagnosticSpec.Create(InvalidServiceImplementation, ToFullName(type));
            }

            return result;
        }

        if (candidates.Count > 1)
        {
            result.Diagnostic = DiagnosticSpec.Create(AmbiguousServiceConstructor, ToFullName(type));
            return result;
        }

        IMethodSymbol constructor = candidates[0];
        result.HasAccessibleConstructor = true;
        foreach (IParameterSymbol parameter in constructor.Parameters)
        {
            bool unresolvable = IsDefinitelyUnresolvable(parameter);
            result.Parameters.Add(CreateParameterSpec(parameter, unresolvable));
        }

        return result;
    }

    private static ParameterSpec CreateParameterSpec(IParameterSymbol parameter, bool unresolvable)
    {
        if (parameter.Type is IArrayTypeSymbol arrayType)
        {
            return new ParameterSpec(ToFullName(arrayType), ToFullName(arrayType.ElementType), unresolvable);
        }

        return new ParameterSpec(ToFullName(parameter.Type), null, unresolvable);
    }

    private static bool IsAccessibleFromHost(INamedTypeSymbol type, IAssemblySymbol? hostAssembly)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            bool sameAssembly = hostAssembly is not null
                && SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, hostAssembly);
            Accessibility accessibility = current.DeclaredAccessibility;
            bool visible = sameAssembly
                ? accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
                : accessibility == Accessibility.Public;
            if (!visible)
            {
                return false;
            }
        }

        return true;
    }

    private static List<IMethodSymbol> SelectVisibleConstructors(INamedTypeSymbol type, IAssemblySymbol? hostAssembly)
    {
        var candidates = new List<IMethodSymbol>();
        foreach (IMethodSymbol candidate in type.Constructors)
        {
            if (candidate.IsStatic)
            {
                continue;
            }

            bool sameAssembly = hostAssembly is not null
                && SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, hostAssembly);
            bool visible = sameAssembly
                ? candidate.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
                : candidate.DeclaredAccessibility == Accessibility.Public;
            if (visible)
            {
                candidates.Add(candidate);
            }
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

    private static List<string> CollectContractNames(
        INamedTypeSymbol exportAttribute,
        INamedTypeSymbol? startableInterface,
        ImmutableArray<AttributeData> attributes,
        INamedTypeSymbol implementation)
    {
        var contracts = new List<string>();
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
                contracts.Add(ToFullName(contract));
            }
            else
            {
                contracts.Add(ToFullName(implementation));
            }
        }

        contracts.Sort(static (left, right) => string.CompareOrdinal(left, right));

        if (startableInterface is not null && ImplementsInterface(implementation, startableInterface))
        {
            contracts.Add(ToFullName(startableInterface));
        }

        // Deduplicate identical contracts while keeping deterministic ordering.
        var unique = new List<string>(contracts.Count);
        for (int index = 0; index < contracts.Count; index++)
        {
            if (index == 0 || contracts[index] != contracts[index - 1])
            {
                unique.Add(contracts[index]);
            }
        }

        return unique;
    }

    private static string ToFullName(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);

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

    // -----------------------------------------------------------------
    // AOT component template roots
    // -----------------------------------------------------------------

    private const string EcsComponentInterfaceName = "DCFApixels.DragonECS.IEcsComponent";
    private const string EcsTagComponentInterfaceName = "DCFApixels.DragonECS.IEcsTagComponent";
    private const string ComponentTemplateDefinitionName = "Karpik.Engine.Shared.ECS.ComponentTemplate`1";
    private const string TagComponentTemplateDefinitionName = "Karpik.Engine.Shared.ECS.TagComponentTemplate`1";

    /// <summary>
    /// NativeAOT cannot compile generic instantiations created by runtime
    /// MakeGenericType calls. The hot-reload state pipeline builds
    /// ComponentTemplate&lt;T&gt;/TagComponentTemplate&lt;T&gt; instances through
    /// reflection for every ECS component in the graph, so the generated
    /// composition statically touches one instantiation per discovered component
    /// type, forcing the native compiler to emit them.
    /// </summary>
    private static string? CollectComponentRoot(INamedTypeSymbol type, Compilation compilation)
    {
        if (!type.IsValueType || type.IsStatic || type.IsGenericType)
        {
            return null;
        }

        INamedTypeSymbol? componentInterface = compilation.GetTypeByMetadataName(EcsComponentInterfaceName);
        INamedTypeSymbol? tagComponentInterface = compilation.GetTypeByMetadataName(EcsTagComponentInterfaceName);
        if (componentInterface is not null
            && ImplementsInterface(type, componentInterface)
            && compilation.GetTypeByMetadataName(ComponentTemplateDefinitionName) is not null)
        {
            return $"typeof(global::Karpik.Engine.Shared.ECS.ComponentTemplate<global::{ToFullName(type)}>)";
        }

        if (tagComponentInterface is not null
            && ImplementsInterface(type, tagComponentInterface)
            && compilation.GetTypeByMetadataName(TagComponentTemplateDefinitionName) is not null)
        {
            return $"typeof(global::Karpik.Engine.Shared.ECS.TagComponentTemplate<global::{ToFullName(type)}>)";
        }

        return null;
    }

    // -----------------------------------------------------------------
    // ECS scheduling descriptor metadata
    // -----------------------------------------------------------------

    private static SchedulingModel ExtractScheduling(INamedTypeSymbol type)
    {
        bool isSequential = false;
        var accesses = new List<SchedulingAccess>();
        var orders = new List<SchedulingOrder>();

        foreach (AttributeData attribute in type.GetAttributes())
        {
            INamedTypeSymbol? attributeType = attribute.AttributeClass;
            if (attributeType is null)
            {
                continue;
            }

            if (attributeType.Name == "SequentialSystemAttribute")
            {
                isSequential = true;
                continue;
            }

            if (attributeType.TypeArguments.Length != 1)
            {
                continue;
            }

            string argument = ToFullName(attributeType.TypeArguments[0]);
            switch (attributeType.Name)
            {
                case "ReadsAttribute":
                    accesses.Add(new SchedulingAccess(argument, "Read"));
                    break;
                case "WritesAttribute":
                    accesses.Add(new SchedulingAccess(argument, "Write"));
                    break;
                case "RunsAfterAttribute":
                    orders.Add(new SchedulingOrder(argument, "After"));
                    break;
                case "RunsBeforeAttribute":
                    orders.Add(new SchedulingOrder(argument, "Before"));
                    break;
            }
        }

        accesses.Sort(static (left, right) => string.CompareOrdinal(left.TypeName, right.TypeName));
        orders.Sort(static (left, right) => string.CompareOrdinal(left.TargetTypeName, right.TargetTypeName));
        return new SchedulingModel(
            SanitizeIdentifier(ToFullName(type)),
            isSequential,
            accesses.ToArray(),
            orders.ToArray());
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    // -----------------------------------------------------------------
    // Emission
    // -----------------------------------------------------------------

    private static string GenerateSource(
        List<InstallerModel> installers,
        List<ServiceModel> registrations,
        List<string> aotComponentTemplateRoots,
        List<SchedulingModel> updateSchedulingSystems,
        List<SchedulingModel> renderPrepareSchedulingSystems)
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
        foreach (InstallerModel installer in installers)
        {
            if (!installer.Valid)
            {
                continue;
            }

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
            foreach (string contract in model.Contracts)
            {
                builder.Append("            registry.Register<global::").Append(contract);
                builder.Append(", global::").Append(model.FullName).Append('>');
                builder.Append("(global::Karpik.Engine.Core.ModuleScope.").Append(ScopeName(model.Scope));
                builder.Append(", global::Karpik.Engine.Core.ServiceLifetime.").Append(LifetimeName(model.Lifetime));
                builder.Append(", ").Append(factory).AppendLine(");");
            }
        }
        if (aotComponentTemplateRoots.Count > 0)
        {
            // RegisterServices is interface-dispatched from the static host, so
            // touching the roots here keeps the field and the generic
            // ComponentTemplate<T> instantiations alive through trimming/AOT.
            builder.AppendLine("            TouchAotComponentTemplateRoots();");
        }
        builder.AppendLine("        }");
        AppendEcsRegistryProviders(builder, updateSchedulingSystems, renderPrepareSchedulingSystems);
        if (aotComponentTemplateRoots.Count > 0)
        {
            // NativeAOT: statically touch the ComponentTemplate<T> / TagComponentTemplate<T>
            // instantiations the reflection-based state pipeline builds at runtime, so the
            // native compiler emits their generic code.
            builder.AppendLine();
            builder.AppendLine("        private static void TouchAotComponentTemplateRoots()");
            builder.AppendLine("        {");
            builder.AppendLine("            for (int i = 0; i < AotComponentTemplateRoots.Length; i++)");
            builder.AppendLine("            {");
            builder.AppendLine("                _ = AotComponentTemplateRoots[i];");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        internal static readonly global::System.Type[] AotComponentTemplateRoots = new global::System.Type[]");
            builder.AppendLine("        {");
            foreach (string root in aotComponentTemplateRoots)
            {
                builder.Append("            ").Append(root).AppendLine(",");
            }
            builder.AppendLine("        };");
        }
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendEcsRegistryProviders(
        StringBuilder builder,
        List<SchedulingModel> updateSchedulingSystems,
        List<SchedulingModel> renderPrepareSchedulingSystems)
    {
        // The Static execution path receives fully constructed registry providers
        // through the composition contract; the runner never enumerates assemblies
        // reflectively in Static mode.
        builder.AppendLine();
        builder.AppendLine("        public void RegisterEcsRegistryProviders(global::Karpik.Engine.Core.IStaticEcsRegistryProviders registry)");
        builder.AppendLine("        {");
        if (updateSchedulingSystems.Count > 0)
        {
            builder.AppendLine("            registry.AddUpdate(new GeneratedEcsUpdateRegistryProvider());");
        }
        if (renderPrepareSchedulingSystems.Count > 0)
        {
            builder.AppendLine("            registry.AddRenderPrepare(new GeneratedEcsRenderPrepareRegistryProvider());");
        }
        builder.AppendLine("        }");

        if (updateSchedulingSystems.Count > 0)
        {
            AppendProviderClass(
                builder,
                "GeneratedEcsUpdateRegistryProvider",
                "IEcsUpdateRegistryProvider",
                "GetUpdateSystems",
                updateSchedulingSystems);
        }

        if (renderPrepareSchedulingSystems.Count > 0)
        {
            AppendProviderClass(
                builder,
                "GeneratedEcsRenderPrepareRegistryProvider",
                "IEcsRenderPrepareRegistryProvider",
                "GetRenderPrepareSystems",
                renderPrepareSchedulingSystems);
        }
    }

    private static void AppendProviderClass(
        StringBuilder builder,
        string className,
        string providerInterfaceName,
        string methodName,
        List<SchedulingModel> systems)
    {
        const string descriptorNamespace = "global::Karpik.Engine.Shared.ECS.Scheduling";
        builder.AppendLine();
        builder.AppendLine($"    internal sealed class {className} : {descriptorNamespace}.{providerInterfaceName}");
        builder.AppendLine("    {");

        foreach (SchedulingModel system in systems)
        {
            builder.AppendLine($"        private static readonly {descriptorNamespace}.EcsComponentAccessDescriptor[] {system.Identifier}Accesses =");
            builder.AppendLine("        {");
            foreach (SchedulingAccess access in system.Accesses)
            {
                builder.AppendLine($"            new {descriptorNamespace}.EcsComponentAccessDescriptor(typeof(global::{access.TypeName}), {descriptorNamespace}.EcsAccessMode.{access.Mode}),");
            }
            builder.AppendLine("        };");
            builder.AppendLine();
            builder.AppendLine($"        private static readonly {descriptorNamespace}.EcsSystemOrderDescriptor[] {system.Identifier}Orders =");
            builder.AppendLine("        {");
            foreach (SchedulingOrder order in system.Orders)
            {
                builder.AppendLine($"            new {descriptorNamespace}.EcsSystemOrderDescriptor(typeof(global::{order.TargetTypeName}), {descriptorNamespace}.EcsOrderKind.{order.Kind}),");
            }
            builder.AppendLine("        };");
            builder.AppendLine();
        }

        builder.AppendLine($"        private static readonly {descriptorNamespace}.EcsUpdateSystemDescriptor[] Systems =");
        builder.AppendLine("        {");
        foreach (SchedulingModel system in systems)
        {
            builder.AppendLine($"            new {descriptorNamespace}.EcsUpdateSystemDescriptor(typeof(global::{system.SystemTypeName}), IsSequential: {system.IsSequential.ToString().ToLowerInvariant()}, {system.Identifier}Accesses, {system.Identifier}Orders),");
        }
        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine($"        public global::System.ReadOnlySpan<{descriptorNamespace}.EcsUpdateSystemDescriptor> {methodName}()");
        builder.AppendLine("        {");
        builder.AppendLine("            return Systems;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static string BuildFactory(ServiceModel model)
    {
        var factory = new StringBuilder();
        factory.Append("static resolver => new global::").Append(model.FullName).Append('(');
        ParameterSpec[] parameters = model.Parameters;
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

    private static string BuildArgument(ParameterSpec parameter)
    {
        if (parameter.ArrayElementTypeName is not null)
        {
            // Autofac resolves element arrays natively; IServiceResolver exposes
            // the same capability through the non-generic Resolve(Type) overload.
            return $"(global::{parameter.TypeFullName})resolver.Resolve(typeof(global::{parameter.ArrayElementTypeName}[]))";
        }

        return $"resolver.Resolve<global::{parameter.TypeFullName}>()";
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

    // -----------------------------------------------------------------
    // Equatable pipeline models
    // -----------------------------------------------------------------

    internal readonly struct CompositionProperties
    {
        internal CompositionProperties(bool anyPresent, string side, string projectKind, string compositionMode)
        {
            AnyPresent = anyPresent;
            Side = side;
            ProjectKind = projectKind;
            CompositionMode = compositionMode;
        }

        internal bool AnyPresent { get; }
        internal string Side { get; }
        internal string ProjectKind { get; }
        internal string CompositionMode { get; }

        internal static CompositionProperties Read(AnalyzerConfigOptions options)
        {
            bool hasSide = options.TryGetValue(SideProperty, out string? side);
            options.TryGetValue(ProjectKindProperty, out string? projectKind);
            bool hasMode = options.TryGetValue(CompositionModeProperty, out string? mode);
            return new CompositionProperties(
                hasSide || hasMode,
                side ?? string.Empty,
                projectKind ?? string.Empty,
                mode ?? string.Empty);
        }
    }

    internal sealed class DiagnosticSpec : IEquatable<DiagnosticSpec>
    {
        private DiagnosticSpec(string id, string[] args)
        {
            Id = id;
            Args = args;
        }

        internal string Id { get; }

        internal string[] Args { get; }

        internal static DiagnosticSpec Create(DiagnosticDescriptor descriptor, params string[] args) =>
            new(descriptor.Id, args);

        internal void ReportTo(SourceProductionContext context, Location? location)
        {
            DiagnosticDescriptor? descriptor = FindDescriptor(Id);
            if (descriptor is null)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(descriptor, location ?? Location.None, Args));
        }

        private static DiagnosticDescriptor? FindDescriptor(string id) => id switch
        {
            "KCORE002" => InvalidModuleInstaller,
            "KE304" => MissingExportAttribute,
            "KE305" => InvalidServiceImplementation,
            "KE306" => AmbiguousServiceConstructor,
            "KE307" => UnresolvableDependency,
            _ => null,
        };

        public bool Equals(DiagnosticSpec? other) =>
            other is not null && Id == other.Id && Args.SequenceEqual(other.Args, StringComparer.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as DiagnosticSpec);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Id.GetHashCode();
                foreach (string arg in Args)
                {
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(arg);
                }

                return hash;
            }
        }
    }

    internal sealed class InstallerModel : IEquatable<InstallerModel>
    {
        internal InstallerModel(
            string fullName,
            int scope,
            int priority,
            string assemblyDisplayName,
            string[] baseChain,
            bool valid,
            DiagnosticSpec[] diagnostics,
            Location? location)
        {
            FullName = fullName;
            Scope = scope;
            Priority = priority;
            AssemblyDisplayName = assemblyDisplayName;
            BaseChain = baseChain;
            Valid = valid;
            Diagnostics = diagnostics;
            Location = location;
        }

        internal string FullName { get; }
        internal int Scope { get; }
        internal int Priority { get; }
        internal string AssemblyDisplayName { get; }
        internal string[] BaseChain { get; }
        internal bool Valid { get; }
        internal DiagnosticSpec[] Diagnostics { get; }
        internal Location? Location { get; }

        internal static InstallerModel Empty(Location? location) => new(
            string.Empty, 0, 0, string.Empty, [], valid: false, [], location);

        internal bool DerivesFrom(InstallerModel other) => BaseChain.Contains(other.FullName);

        internal void ReportTo(SourceProductionContext context)
        {
            foreach (DiagnosticSpec diagnostic in Diagnostics)
            {
                diagnostic.ReportTo(context, Location);
            }
        }

        public bool Equals(InstallerModel? other) =>
            other is not null
            && FullName == other.FullName
            && Scope == other.Scope
            && Priority == other.Priority
            && AssemblyDisplayName == other.AssemblyDisplayName
            && Valid == other.Valid
            && BaseChain.SequenceEqual(other.BaseChain, StringComparer.Ordinal)
            && Diagnostics.SequenceEqual(other.Diagnostics);

        public override bool Equals(object? obj) => Equals(obj as InstallerModel);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(FullName);
    }

    internal sealed class ParameterSpec : IEquatable<ParameterSpec>
    {
        internal ParameterSpec(string typeFullName, string? arrayElementTypeName, bool isDefinitelyUnresolvable)
        {
            TypeFullName = typeFullName;
            ArrayElementTypeName = arrayElementTypeName;
            IsDefinitelyUnresolvable = isDefinitelyUnresolvable;
        }

        internal string TypeFullName { get; }
        internal string? ArrayElementTypeName { get; }
        internal bool IsDefinitelyUnresolvable { get; }

        public bool Equals(ParameterSpec? other) =>
            other is not null
            && TypeFullName == other.TypeFullName
            && ArrayElementTypeName == other.ArrayElementTypeName
            && IsDefinitelyUnresolvable == other.IsDefinitelyUnresolvable;

        public override bool Equals(object? obj) => Equals(obj as ParameterSpec);

        public override int GetHashCode() => TypeFullName.GetHashCode();
    }

    internal sealed class ServiceModel : IEquatable<ServiceModel>
    {
        internal ServiceModel(
            string fullName,
            string assemblyDisplayName,
            int scope,
            int lifetime,
            List<string> contracts,
            ParameterSpec[] parameters,
            bool isStartable,
            bool emitted,
            DiagnosticSpec[] diagnostics,
            Location? location)
        {
            FullName = fullName;
            AssemblyDisplayName = assemblyDisplayName;
            Scope = scope;
            Lifetime = lifetime;
            Contracts = contracts;
            Parameters = parameters;
            IsStartable = isStartable;
            Emitted = emitted;
            Diagnostics = diagnostics;
            Location = location;
        }

        internal string FullName { get; }
        internal string AssemblyDisplayName { get; }
        internal int Scope { get; }
        internal int Lifetime { get; }
        internal List<string> Contracts { get; }
        internal ParameterSpec[] Parameters { get; }
        internal bool IsStartable { get; }
        internal bool Emitted { get; }
        internal DiagnosticSpec[] Diagnostics { get; }
        internal Location? Location { get; }

        internal static ServiceModel Empty(Location? location) => new(
            string.Empty, string.Empty, 0, 0, [], [], false, false, [], location);

        internal static ServiceModel Invalid(Location? location, params DiagnosticSpec[] diagnostics) => new(
            string.Empty, string.Empty, 0, 0, [], [], false, false, diagnostics, location);

        internal void ReportDiagnostics(SourceProductionContext context)
        {
            foreach (DiagnosticSpec diagnostic in Diagnostics)
            {
                diagnostic.ReportTo(context, Location);
            }
        }

        public bool Equals(ServiceModel? other) =>
            other is not null
            && FullName == other.FullName
            && AssemblyDisplayName == other.AssemblyDisplayName
            && Scope == other.Scope
            && Lifetime == other.Lifetime
            && IsStartable == other.IsStartable
            && Emitted == other.Emitted
            && Contracts.SequenceEqual(other.Contracts, StringComparer.Ordinal)
            && Parameters.SequenceEqual(other.Parameters)
            && Diagnostics.SequenceEqual(other.Diagnostics);

        public override bool Equals(object? obj) => Equals(obj as ServiceModel);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(FullName);
    }

    internal sealed class SystemModel : IEquatable<SystemModel>
    {
        internal SystemModel(ServiceModel? model, bool isUpdate, bool isRenderPrepare, SchedulingModel scheduling)
        {
            Model = model;
            IsUpdateSystem = isUpdate;
            IsRenderPrepareSystem = isRenderPrepare;
            Scheduling = scheduling;
        }

        internal ServiceModel? Model { get; }
        internal bool IsUpdateSystem { get; }
        internal bool IsRenderPrepareSystem { get; }
        internal SchedulingModel Scheduling { get; }

        internal static SystemModel Empty => new(null, false, false, SchedulingModel.Empty);

        public bool Equals(SystemModel? other) =>
            other is not null
            && IsUpdateSystem == other.IsUpdateSystem
            && IsRenderPrepareSystem == other.IsRenderPrepareSystem
            && Scheduling.Equals(other.Scheduling)
            && (Model is null ? other.Model is null : Model.Equals(other.Model));

        public override bool Equals(object? obj) => Equals(obj as SystemModel);

        public override int GetHashCode() => Model?.GetHashCode() ?? 0;
    }

    internal sealed class SchedulingModel : IEquatable<SchedulingModel>
    {
        internal SchedulingModel(
            string identifier,
            bool isSequential,
            SchedulingAccess[] accesses,
            SchedulingOrder[] orders)
        {
            Identifier = identifier;
            IsSequential = isSequential;
            Accesses = accesses;
            Orders = orders;
        }

        internal string Identifier { get; }
        internal bool IsSequential { get; }
        internal SchedulingAccess[] Accesses { get; }
        internal SchedulingOrder[] Orders { get; }

        internal string SystemTypeName => Identifier.Replace('_', '.');

        internal static SchedulingModel Empty => new(string.Empty, false, [], []);

        public bool Equals(SchedulingModel? other) =>
            other is not null
            && Identifier == other.Identifier
            && IsSequential == other.IsSequential
            && Accesses.SequenceEqual(other.Accesses)
            && Orders.SequenceEqual(other.Orders);

        public override bool Equals(object? obj) => Equals(obj as SchedulingModel);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Identifier);
    }

    internal readonly struct SchedulingAccess : IEquatable<SchedulingAccess>
    {
        internal SchedulingAccess(string typeName, string mode)
        {
            TypeName = typeName;
            Mode = mode;
        }

        internal string TypeName { get; }
        internal string Mode { get; }

        public bool Equals(SchedulingAccess other) => TypeName == other.TypeName && Mode == other.Mode;

        public override bool Equals(object? obj) => obj is SchedulingAccess other && Equals(other);

        public override int GetHashCode() => TypeName.GetHashCode();
    }

    internal readonly struct SchedulingOrder : IEquatable<SchedulingOrder>
    {
        internal SchedulingOrder(string targetTypeName, string kind)
        {
            TargetTypeName = targetTypeName;
            Kind = kind;
        }

        internal string TargetTypeName { get; }
        internal string Kind { get; }

        public bool Equals(SchedulingOrder other) => TargetTypeName == other.TargetTypeName && Kind == other.Kind;

        public override bool Equals(object? obj) => obj is SchedulingOrder other && Equals(other);

        public override int GetHashCode() => TargetTypeName.GetHashCode();
    }

    internal sealed class ReferencedModels
    {
        internal ReferencedModels(
            ImmutableArray<InstallerModel> installers,
            ImmutableArray<ServiceModel> services,
            ImmutableArray<SystemModel> systems,
            ImmutableArray<ComponentRootModel> components)
        {
            Installers = installers;
            Services = services;
            Systems = systems;
            Components = components;
        }

        internal ImmutableArray<InstallerModel> Installers { get; }
        internal ImmutableArray<ServiceModel> Services { get; }
        internal ImmutableArray<SystemModel> Systems { get; }
        internal ImmutableArray<ComponentRootModel> Components { get; }
    }

    internal sealed class ComponentRootModel : IEquatable<ComponentRootModel>
    {
        internal ComponentRootModel(string rootExpression)
        {
            RootExpression = rootExpression;
        }

        internal string RootExpression { get; }

        internal static ComponentRootModel Empty => new(string.Empty);

        public bool Equals(ComponentRootModel? other) =>
            other is not null && RootExpression == other.RootExpression;

        public override bool Equals(object? obj) => Equals(obj as ComponentRootModel);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(RootExpression);
    }
}
#pragma warning restore RS2008

