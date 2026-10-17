namespace Karpik.Engine.Tooling;

public readonly record struct EngineModuleSelection(string LogicalModuleId, bool Enabled, string? Implementation);

public enum EngineModuleSelectionErrorCode
{
    SelectionMetadataMissing,
    UnknownLogicalModule,
    InvalidImplementation,
    ConflictingImplementation,
    MissingRequiredDependency,
    SideLeak
}

public sealed class EngineModuleSelectionException : Exception
{
    public EngineModuleSelectionErrorCode Code { get; }

    public EngineModuleSelectionException(EngineModuleSelectionErrorCode code, string message)
        : base(message) => Code = code;
}

public static class EngineModuleSelectionResolver
{
    public static EngineModuleCatalogEntry[] Resolve(
        IEnumerable<EngineModuleCatalogEntry> catalog,
        IEnumerable<EngineModuleSelection> selections,
        EngineModuleSide side)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selections);
        EngineModuleSelection[] selectionItems = selections.ToArray();
        var disabledModules = selectionItems.Where(selection => !selection.Enabled)
            .Select(selection => selection.LogicalModuleId).ToHashSet(StringComparer.Ordinal);
        EngineModuleCatalogEntry[] entries = catalog.ToArray();
        if (entries.Any(entry => !entry.HasSelectionMetadata))
        {
            throw new EngineModuleSelectionException(
                EngineModuleSelectionErrorCode.SelectionMetadataMissing,
                "The engine module catalog does not contain selection metadata; install a layout-v4 catalog.");
        }
        _ = EngineModuleCatalog.Serialize(entries);

        var groups = entries
            .GroupBy(entry => entry.LogicalModuleId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var byId = entries.ToDictionary(entry => entry.ModuleId, ModuleLayoutPolicy.ModuleIdComparer);
        var selected = new HashSet<string>(ModuleLayoutPolicy.ModuleIdComparer);
        var requestedImplementations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (EngineModuleSelection selection in selectionItems)
        {
            if (!groups.TryGetValue(selection.LogicalModuleId, out EngineModuleCatalogEntry[]? group))
            {
                throw Error(EngineModuleSelectionErrorCode.UnknownLogicalModule, $"Unknown logical engine module: {selection.LogicalModuleId}");
            }
            if (!selection.Enabled)
            {
                continue;
            }
            if (!requestedImplementations.TryAdd(selection.LogicalModuleId, selection.Implementation ?? string.Empty))
            {
                throw Error(EngineModuleSelectionErrorCode.ConflictingImplementation, $"Duplicate selection for logical module: {selection.LogicalModuleId}");
            }

            EngineModuleCatalogEntry[] available = group.Where(entry => IsAvailable(entry, side)).ToArray();
            if (available.Length == 0)
            {
                throw Error(EngineModuleSelectionErrorCode.SideLeak, $"Logical module '{selection.LogicalModuleId}' is not available on {side}.");
            }
            if (available.Any(entry => entry.Kind == EngineModuleKind.Implementation))
            {
                if (string.IsNullOrWhiteSpace(selection.Implementation))
                {
                    throw Error(EngineModuleSelectionErrorCode.InvalidImplementation, $"Logical module '{selection.LogicalModuleId}' requires one implementation.");
                }
                EngineModuleCatalogEntry[] implementations = available
                    .Where(entry => entry.Kind == EngineModuleKind.Implementation && entry.ImplementationId == selection.Implementation)
                    .ToArray();
                if (implementations.Length != 1)
                {
                    if (group.Any(entry => entry.ImplementationId == selection.Implementation))
                    {
                        throw Error(EngineModuleSelectionErrorCode.SideLeak, $"Implementation '{selection.Implementation}' of '{selection.LogicalModuleId}' is not available on {side}.");
                    }
                    throw Error(EngineModuleSelectionErrorCode.InvalidImplementation, $"Invalid implementation '{selection.Implementation}' for logical module '{selection.LogicalModuleId}'.");
                }
                Add(implementations[0]);
            }
            else if (selection.Implementation is not null)
            {
                throw Error(EngineModuleSelectionErrorCode.InvalidImplementation, $"Logical module '{selection.LogicalModuleId}' has no implementations.");
            }

            foreach (EngineModuleCatalogEntry entry in available.Where(entry => entry.Kind != EngineModuleKind.Implementation))
            {
                Add(entry);
            }
        }

        return selected
            .Select(moduleId => byId[moduleId])
            .OrderBy(entry => entry.Side)
            .ThenBy(entry => entry.ModuleId, StringComparer.Ordinal)
            .ToArray();

        void Add(EngineModuleCatalogEntry entry)
        {
            if (!IsAvailable(entry, side))
            {
                throw Error(EngineModuleSelectionErrorCode.SideLeak, $"Module '{entry.ModuleId}' is not available on {side}.");
            }
            if (entry.Kind == EngineModuleKind.Implementation &&
                requestedImplementations.TryGetValue(entry.LogicalModuleId!, out string? requested) &&
                !string.Equals(requested, entry.ImplementationId, StringComparison.Ordinal))
            {
                throw Error(EngineModuleSelectionErrorCode.ConflictingImplementation, $"Conflicting implementations for logical module '{entry.LogicalModuleId}'.");
            }
            if (!selected.Add(entry.ModuleId))
            {
                return;
            }
            foreach (EngineModuleDependency dependency in entry.Dependencies)
            {
                // Optional edges describe integrations with an independently selected
                // module; they never expand the selected graph or choose its backend.
                if (dependency.Optional)
                {
                    continue;
                }
                if (!byId.TryGetValue(dependency.ModuleId, out EngineModuleCatalogEntry dependencyEntry))
                {
                    throw Error(EngineModuleSelectionErrorCode.MissingRequiredDependency, $"Module '{entry.ModuleId}' requires missing module '{dependency.ModuleId}'.");
                }
                if (!IsAvailable(dependencyEntry, side))
                {
                    throw Error(EngineModuleSelectionErrorCode.SideLeak, $"Module '{entry.ModuleId}' requires {side}-incompatible module '{dependency.ModuleId}'.");
                }
                if (disabledModules.Contains(dependencyEntry.LogicalModuleId!))
                {
                    throw Error(EngineModuleSelectionErrorCode.MissingRequiredDependency,
                        $"Module '{entry.ModuleId}' requires explicitly disabled module '{dependency.ModuleId}'.");
                }
                Add(dependencyEntry);
            }
        }
    }

    private static bool IsAvailable(EngineModuleCatalogEntry entry, EngineModuleSide side) =>
        side is EngineModuleSide.Shared
            ? entry.Side is EngineModuleSide.Shared
            : entry.Side is EngineModuleSide.Shared || entry.Side == side;

    private static EngineModuleSelectionException Error(EngineModuleSelectionErrorCode code, string message) =>
        new(code, message);
}
