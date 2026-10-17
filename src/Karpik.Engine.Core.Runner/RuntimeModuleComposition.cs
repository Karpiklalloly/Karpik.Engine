using Karpik.Engine.Tooling;

namespace Karpik.Engine.Core.Runner;

/// <summary>
/// Dynamic composition path only: resolves the installed module catalog for the
/// universal reflection-based runner. Static hosts use generated
/// <c>GeneratedRuntimeComposition</c> instead and must never call this API.
/// Kept until the default composition mode flips to Static.
/// </summary>
public static class RuntimeModuleComposition
{
    public static ModuleLoader.EngineModuleDescriptor[] Resolve(string engineRoot, Side side)
    {
        EngineModuleSide moduleSide = ParseSide(side);
        string modulesRoot = Path.Combine(engineRoot, "modules");
        EngineModuleCatalogEntry[] selected = EngineModuleCatalog.ForSide(
            EngineModuleCatalog.Read(modulesRoot),
            moduleSide);
        if (!selected.Any(entry => entry is { Side: EngineModuleSide.Shared, ModuleId: "ECS.Core" }))
        {
            throw new InvalidDataException("The installed engine runtime must provide shared module ECS.Core.");
        }
        return selected.Select(entry => new ModuleLoader.EngineModuleDescriptor(
                entry.ModuleId,
                Path.Combine(modulesRoot, entry.ModuleId)))
            .ToArray();
    }

    public static ModuleLoader.EngineModuleDescriptor[] Resolve(string engineRoot, string bundleRoot, Side side)
    {
        EngineModuleSide moduleSide = ParseSide(side);
        string modulesRoot = Path.Combine(engineRoot, "modules");
        EngineModuleCatalogEntry[] catalog = EngineModuleCatalog.Read(modulesRoot);
        if (catalog.Any(entry => !entry.HasSelectionMetadata))
        {
            throw new InvalidDataException("The engine module catalog does not contain selection metadata required by the dynamic bundle.");
        }

        string moduleDirectory = RuntimeBundleLayout.ResolveModuleDirectory(bundleRoot);
        string[] selectedIds = RuntimeBundleLayout.ReadCanonicalEngineModuleManifest(moduleDirectory);
        var byId = catalog.ToDictionary(entry => entry.ModuleId, ModuleLayoutPolicy.ModuleIdComparer);
        var selected = new List<EngineModuleCatalogEntry>(selectedIds.Length);
        var selectedSet = selectedIds.ToHashSet(ModuleLayoutPolicy.ModuleIdComparer);
        foreach (string id in selectedIds)
        {
            if (!byId.TryGetValue(id, out EngineModuleCatalogEntry entry))
            {
                throw new InvalidDataException($"Dynamic engine module manifest references unknown module '{id}'.");
            }
            if (entry.Side is not EngineModuleSide.Shared && entry.Side != moduleSide)
            {
                throw new InvalidDataException($"Dynamic engine module manifest references {entry.Side}-only module '{id}' on {moduleSide} runtime.");
            }
            selected.Add(entry);
        }
        foreach (IGrouping<string, EngineModuleCatalogEntry> logicalGroup in selected
                     .GroupBy(entry => entry.LogicalModuleId!, StringComparer.Ordinal))
        {
            bool hasImplementationChoice = catalog.Any(entry =>
                entry.Kind == EngineModuleKind.Implementation &&
                string.Equals(entry.LogicalModuleId, logicalGroup.Key, StringComparison.Ordinal) &&
                IsAvailable(entry, moduleSide));
            if (hasImplementationChoice && logicalGroup.Count(entry => entry.Kind == EngineModuleKind.Implementation) != 1)
            {
                throw new InvalidDataException($"Dynamic engine module manifest must select exactly one implementation for '{logicalGroup.Key}'.");
            }
        }
        if (!selected.Any(entry => entry is { ModuleId: "ECS.Core", Side: EngineModuleSide.Shared }))
        {
            throw new InvalidDataException("The selected engine module graph must include shared ECS.Core.");
        }
        foreach (EngineModuleCatalogEntry entry in selected)
        {
            if (entry.Kind == EngineModuleKind.Implementation &&
                selected.Count(candidate => candidate.LogicalModuleId == entry.LogicalModuleId && candidate.Kind == EngineModuleKind.Implementation) != 1)
            {
                throw new InvalidDataException($"Dynamic engine module manifest selects conflicting implementations for '{entry.LogicalModuleId}'.");
            }
            foreach (EngineModuleDependency dependency in entry.Dependencies)
            {
                if (!byId.TryGetValue(dependency.ModuleId, out EngineModuleCatalogEntry dependencyEntry))
                {
                    if (!dependency.Optional)
                    {
                        throw new InvalidDataException($"Dynamic engine module manifest has missing required dependency '{dependency.ModuleId}' for '{entry.ModuleId}'.");
                    }
                    continue;
                }
                if (dependencyEntry.Side is not EngineModuleSide.Shared && dependencyEntry.Side != moduleSide)
                {
                    throw new InvalidDataException($"Dynamic engine module manifest has a side-incompatible dependency '{dependency.ModuleId}'.");
                }
                if (!dependency.Optional && !selectedSet.Contains(dependency.ModuleId))
                {
                    throw new InvalidDataException($"Dynamic engine module manifest omits required dependency '{dependency.ModuleId}' for '{entry.ModuleId}'.");
                }
            }
        }
        return selected
            .OrderBy(entry => entry.ModuleId, StringComparer.Ordinal)
            .Select(CreateDescriptor)
            .ToArray();

        ModuleLoader.EngineModuleDescriptor CreateDescriptor(EngineModuleCatalogEntry entry)
        {
            string directory = Path.Combine(modulesRoot, entry.ModuleId);
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"Selected engine module directory does not exist: {directory}");
            }
            RuntimeBundleLayout.EnsureExistingPathHasNoReparsePoints(directory);
            string primary = Path.Combine(directory, entry.ModuleId + ".dll");
            if (!File.Exists(primary))
            {
                throw new FileNotFoundException($"Selected engine module primary assembly is missing: {primary}", primary);
            }
            return new ModuleLoader.EngineModuleDescriptor(entry.ModuleId, directory);
        }
    }

    private static EngineModuleSide ParseSide(Side side) => side switch
    {
        Side.Client => EngineModuleSide.Client,
        Side.Server => EngineModuleSide.Server,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Only Client and Server runtimes are supported.")
    };

    private static bool IsAvailable(EngineModuleCatalogEntry entry, EngineModuleSide side) =>
        entry.Side is EngineModuleSide.Shared || entry.Side == side;

    public static void ValidateRequiredInstallers(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        bool hasEcsInstaller = types.Any(type =>
            type.Assembly.GetName().Name == "ECS.Core" &&
            !type.IsAbstract &&
            typeof(IModuleInstaller).IsAssignableFrom(type) &&
            type.GetCustomAttributes(typeof(ModuleAttribute), inherit: false).Length == 1);
        if (!hasEcsInstaller)
        {
            throw new InvalidDataException("Engine runtime composition is missing an ECS.Core module installer.");
        }
    }
}
