namespace Karpik.Content.Core;

public enum ContentAssetChangeKind
{
    Added = 1,
    Updated = 2,
    Removed = 3
}

/// <summary>One asset's change between two published manifests; contains no managed references.</summary>
public readonly record struct ContentAssetChange(AssetId AssetId, ContentAssetChangeKind Kind);

/// <summary>Build/control-plane comparison. Allocates working arrays and must not run in frame hot paths.</summary>
public static class ContentManifestDiff
{
    /// <summary>
    /// Returns changes in ascending <see cref="AssetId"/> order without modifying either input.
    /// A null previous manifest represents the first publication. All canonical entry fields are compared;
    /// entry and dependency ordering are ignored, but dependency multiplicity is preserved.
    /// This does not validate dependency graphs, publish notifications or update runtime resources.
    /// </summary>
    /// <exception cref="InvalidDataException">Unsupported schema, duplicate IDs or null entries/dependencies.</exception>
    public static ContentAssetChange[] Compare(ContentManifest? previous, ContentManifest current)
    {
        ArgumentNullException.ThrowIfNull(current);
        ContentManifestEntry[] before = previous is null ? [] : SortedEntries(previous);
        ContentManifestEntry[] after = SortedEntries(current);
        var changes = new List<ContentAssetChange>();
        int oldIndex = 0;
        int newIndex = 0;

        while (oldIndex < before.Length || newIndex < after.Length)
        {
            if (oldIndex == before.Length)
            {
                changes.Add(new(after[newIndex++].AssetId, ContentAssetChangeKind.Added));
            }
            else if (newIndex == after.Length)
            {
                changes.Add(new(before[oldIndex++].AssetId, ContentAssetChangeKind.Removed));
            }
            else
            {
                ContentManifestEntry oldEntry = before[oldIndex];
                ContentManifestEntry newEntry = after[newIndex];
                int comparison = oldEntry.AssetId.CompareTo(newEntry.AssetId);
                if (comparison < 0)
                {
                    changes.Add(new(oldEntry.AssetId, ContentAssetChangeKind.Removed));
                    oldIndex++;
                }
                else if (comparison > 0)
                {
                    changes.Add(new(newEntry.AssetId, ContentAssetChangeKind.Added));
                    newIndex++;
                }
                else
                {
                    if (!SameEntry(oldEntry, newEntry))
                        changes.Add(new(newEntry.AssetId, ContentAssetChangeKind.Updated));
                    oldIndex++;
                    newIndex++;
                }
            }
        }

        return changes.ToArray();
    }

    private static ContentManifestEntry[] SortedEntries(ContentManifest manifest)
    {
        if (manifest.SchemaVersion != ContentManifest.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported manifest schema version '{manifest.SchemaVersion}'.");
        if (manifest.Entries is null)
            throw new InvalidDataException("Manifest entries must not be null.");

        ContentManifestEntry[] entries = manifest.Entries.ToArray();
        if (Array.Exists(entries, static e => e is null || e.Dependencies is null))
            throw new InvalidDataException("Manifest entries and their dependencies must not be null.");
        Array.Sort(entries, static (left, right) => left.AssetId.CompareTo(right.AssetId));
        for (int i = 1; i < entries.Length; i++)
            if (entries[i - 1].AssetId == entries[i].AssetId)
                throw new InvalidDataException($"Duplicate AssetId '{entries[i].AssetId}' in manifest.");
        return entries;
    }

    private static bool SameEntry(ContentManifestEntry before, ContentManifestEntry after) =>
        before.DeclaredType == after.DeclaredType && before.LogicalName == after.LogicalName &&
        before.ImportSettingsHash == after.ImportSettingsHash && before.SourceHash == after.SourceHash &&
        before.ArtifactLocator == after.ArtifactLocator && before.Size == after.Size &&
        SameDependencies(before.Dependencies, after.Dependencies);

    private static bool SameDependencies(IReadOnlyList<AssetId> before, IReadOnlyList<AssetId> after)
    {
        if (before.Count != after.Count) return false;
        if (before.SequenceEqual(after)) return true;
        AssetId[] oldIds = before.ToArray();
        AssetId[] newIds = after.ToArray();
        Array.Sort(oldIds);
        Array.Sort(newIds);
        return oldIds.AsSpan().SequenceEqual(newIds);
    }
}
