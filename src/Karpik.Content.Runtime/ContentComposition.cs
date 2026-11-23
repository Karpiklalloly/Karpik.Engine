using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

// Cold input for a single effective ID. Entry retains the original source ID,
// even when the dictionary key is an override target.
internal readonly record struct ContentSource(ContentManifestEntry Entry, IContentStore Store, string? ModId = null);

internal sealed class ContentComposition(Dictionary<AssetId, ContentSource> sources,
    IReadOnlyList<ModContentPackage> packages, HashSet<string> installedModIds)
{
    internal Dictionary<AssetId, ContentSource> Sources { get; } = sources;
    internal IReadOnlyList<ModContentPackage> Packages { get; } = packages;
    internal HashSet<string> InstalledModIds { get; } = installedModIds;

    internal static ContentComposition Build(ContentManifestEntry[] baseEntries, IContentStore baseStore,
        IReadOnlyList<ModContentPackage> packages, IReadOnlyCollection<string> installedModIds,
        bool tolerateInvalidPackages, Action<ContentDiagnostic>? report, CancellationToken ct = default)
    {
        var originals = baseEntries.ToDictionary(entry => entry.AssetId, entry => new ContentSource(entry, baseStore));
        var installed = new HashSet<string>(installedModIds, StringComparer.Ordinal);
        if (packages.Count == 0) return new(originals, [], installed);

        var unique = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            installed.Add(package.ModId);
            if (!unique.Add(package.ModId)) duplicates.Add(package.ModId);
        }

        var accepted = new List<ModContentPackage>(packages.Count);
        foreach (var package in packages)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (string.IsNullOrWhiteSpace(package.ModId) || duplicates.Contains(package.ModId))
                    throw new InvalidDataException($"Missing or duplicate mod ID '{package.ModId}'.");
                var entries = Normalize(package.Manifest);
                if (entries.Length > ContentRegistry.MaximumAssets - originals.Count)
                    throw new InvalidDataException("Composed asset budget exceeded.");
                foreach (var entry in entries)
                    if (originals.ContainsKey(entry.AssetId))
                        throw new InvalidDataException($"Original asset ID {entry.AssetId} already has an owner.");
                foreach (var entry in entries) originals.Add(entry.AssetId, new(entry, package.Store, package.ModId));
                accepted.Add(package);
            }
            catch (InvalidDataException error)
            {
                report?.Invoke(new("KCR302", ContentDiagnosticSeverity.Error, package.SourcePath,
                    $"Invalid Content package '{package.ModId}': {error.Message}"));
                if (!tolerateInvalidPackages) throw;
            }
        }

        var effective = new Dictionary<AssetId, ContentSource>(originals);
        var seen = new HashSet<AssetId>();
        var pending = new Stack<AssetId>();
        foreach (var package in accepted)
        foreach (var rule in package.Overrides)
        {
            ct.ThrowIfCancellationRequested();
            string reason;
            var severity = ContentDiagnosticSeverity.Error;
            if (rule.TargetModId != null && !installed.Contains(rule.TargetModId))
            {
                severity = ContentDiagnosticSeverity.Warning;
                reason = $"Target owner '{rule.TargetModId}' is not installed.";
            }
            else if (!originals.TryGetValue(rule.TargetAssetId, out var target) || target.ModId != rule.TargetModId)
                reason = $"No original asset with this ID belongs to '{rule.TargetModId ?? "game"}'.";
            else if (!originals.TryGetValue(rule.ReplacementAssetId, out var replacement) || replacement.ModId != package.ModId)
                reason = $"Replacement {rule.ReplacementAssetId} is not an original asset of '{package.ModId}'.";
            else if (target.Entry.DeclaredType != replacement.Entry.DeclaredType)
                reason = $"Replacement {rule.ReplacementAssetId} has incompatible declared type '{replacement.Entry.DeclaredType}' (expected '{target.Entry.DeclaredType}').";
            else if (CreatesCycle(effective, rule.TargetAssetId, replacement.Entry.Dependencies, seen, pending))
                reason = $"Replacement {rule.ReplacementAssetId} would create a dependency cycle.";
            else
            {
                effective[rule.TargetAssetId] = replacement;
                continue;
            }

            report?.Invoke(new("KCR303", severity, Path.Combine(package.SourcePath, "overrides.json"),
                $"Override for {rule.TargetAssetId} from '{package.ModId}' skipped: {reason}"));
        }
        return new(effective, accepted.AsReadOnly(), installed);
    }

    private static bool CreatesCycle(Dictionary<AssetId, ContentSource> effective, AssetId target,
        IReadOnlyList<AssetId> dependencies, HashSet<AssetId> seen, Stack<AssetId> pending)
    {
        // Only the target's outgoing edges change: the candidate cycles iff a
        // new edge points back to the target through the currently effective graph.
        if (dependencies.Count == 0) return false;
        seen.Clear();
        pending.Clear();
        foreach (var dependency in dependencies)
            if (seen.Add(dependency)) pending.Push(dependency);
        while (pending.TryPop(out var id))
        {
            if (id == target) return true;
            foreach (var dependency in effective[id].Entry.Dependencies)
                if (seen.Add(dependency)) pending.Push(dependency);
        }
        return false;
    }

    internal static ContentManifestEntry[] Normalize(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != ContentManifest.CurrentSchemaVersion || manifest.Entries == null ||
            manifest.Entries.Count > ContentRegistry.MaximumAssets)
            throw new InvalidDataException("Unsupported manifest schema or asset budget exceeded.");
        var byId = new Dictionary<AssetId, ContentManifestEntry>(manifest.Entries.Count);
        foreach (var e in manifest.Entries)
        {
            if (e == null || e.Dependencies == null || e.Dependencies.Count > ContentRegistry.MaximumAssets ||
                e.Size < 0 || string.IsNullOrWhiteSpace(e.ArtifactLocator) || string.IsNullOrWhiteSpace(e.DeclaredType) ||
                !byId.TryAdd(e.AssetId, new ContentManifestEntry(e.AssetId, e.DeclaredType, e.LogicalName,
                    e.ImportSettingsHash, e.SourceHash, e.ArtifactLocator, e.Size, e.Dependencies.OrderBy(d => d.Value).Distinct().ToArray())))
                throw new InvalidDataException("Invalid or duplicate manifest entry.");
        }
        var states = new Dictionary<AssetId, byte>();
        void Visit(AssetId id)
        {
            if (!byId.TryGetValue(id, out var entry)) throw new InvalidDataException("Manifest dependency is missing.");
            if (states.TryGetValue(id, out byte state))
            {
                if (state == 1) throw new InvalidDataException("Manifest dependency cycle.");
                return;
            }
            states[id] = 1;
            foreach (var dependency in entry.Dependencies) Visit(dependency);
            states[id] = 2;
        }
        foreach (var id in byId.Keys) Visit(id);
        return byId.Values.OrderBy(e => e.AssetId).ToArray();
    }
}
