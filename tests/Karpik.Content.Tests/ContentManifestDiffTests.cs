using System.Runtime.CompilerServices;
using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class ContentManifestDiffTests
{
    private static AssetId Id(int value) => new(new Guid(value, 0, 0, new byte[8]));
    private static ContentManifest Manifest(params ContentManifestEntry[] entries) => new(ContentManifest.CurrentSchemaVersion, entries);
    private static ContentManifestEntry Entry(int id, string revision = "1", params AssetId[] dependencies) =>
        new(Id(id), "raw-json", $"game/{id}", "settings", revision, $"artifacts/{id}-{revision}.cooked", 2, dependencies);

    [Fact]
    public void Compare_MixedChanges_ReturnsOneChangePerIdInStableOrder()
    {
        ContentManifest previous = Manifest(Entry(4), Entry(2), Entry(1));
        ContentManifest current = Manifest(Entry(3), Entry(2, "2"), Entry(4));

        ContentAssetChange[] changes = ContentManifestDiff.Compare(previous, current);

        Assert.Equal(new[]
        {
            new ContentAssetChange(Id(1), ContentAssetChangeKind.Removed),
            new ContentAssetChange(Id(2), ContentAssetChangeKind.Updated),
            new ContentAssetChange(Id(3), ContentAssetChangeKind.Added)
        }, changes);
    }

    [Fact]
    public void Compare_NoPreviousManifest_ReportsAllEntriesAsAdded()
    {
        Assert.Equal(new[]
        {
            new ContentAssetChange(Id(1), ContentAssetChangeKind.Added),
            new ContentAssetChange(Id(2), ContentAssetChangeKind.Added)
        }, ContentManifestDiff.Compare(null, Manifest(Entry(2), Entry(1))));
    }

    [Fact]
    public void Compare_EmptyCurrentManifest_ReportsAllEntriesAsRemoved()
    {
        Assert.Equal(new[]
        {
            new ContentAssetChange(Id(1), ContentAssetChangeKind.Removed),
            new ContentAssetChange(Id(2), ContentAssetChangeKind.Removed)
        }, ContentManifestDiff.Compare(Manifest(Entry(2), Entry(1)), Manifest()));
    }

    [Fact]
    public void Compare_EmptyOrIdenticalManifests_ReturnsNoChanges()
    {
        Assert.Empty(ContentManifestDiff.Compare(null, Manifest()));
        Assert.Empty(ContentManifestDiff.Compare(Manifest(), Manifest()));
        Assert.Empty(ContentManifestDiff.Compare(Manifest(Entry(1)), Manifest(Entry(1))));
    }

    [Fact]
    public void Compare_EntryAndDependencyOrderOnly_DoesNotChangeOrMutateInputs()
    {
        ContentManifestEntry[] previous = [Entry(2), Entry(1, "1", Id(4), Id(3))];
        ContentManifestEntry[] current = [Entry(1, "1", Id(3), Id(4)), Entry(2)];
        ContentManifestEntry[] originalOrder = previous.ToArray();
        AssetId[] originalDependencies = previous[1].Dependencies.ToArray();

        Assert.Empty(ContentManifestDiff.Compare(Manifest(previous), Manifest(current)));

        Assert.Equal(originalOrder, previous);
        Assert.Equal(originalDependencies, previous[1].Dependencies);
        Assert.Equal(Id(1), current[0].AssetId);
    }

    [Theory]
    [InlineData("declaredType")]
    [InlineData("logicalName")]
    [InlineData("importSettingsHash")]
    [InlineData("sourceHash")]
    [InlineData("artifactLocator")]
    [InlineData("size")]
    [InlineData("dependencies")]
    [InlineData("dependencyMultiplicity")]
    public void Compare_AnyCanonicalEntryFieldChanges_ReportsUpdated(string field)
    {
        ContentManifestEntry original = Entry(1, "1", Id(2));
        var updated = new ContentManifestEntry(original.AssetId,
            field == "declaredType" ? "texture" : original.DeclaredType,
            field == "logicalName" ? "game/renamed" : original.LogicalName,
            field == "importSettingsHash" ? "new-settings" : original.ImportSettingsHash,
            field == "sourceHash" ? "new-source" : original.SourceHash,
            field == "artifactLocator" ? "artifacts/new.cooked" : original.ArtifactLocator,
            field == "size" ? 3 : original.Size,
            field == "dependencies" ? [Id(3)] : field == "dependencyMultiplicity" ? [Id(2), Id(2)] : original.Dependencies);

        Assert.Equal(new[] { new ContentAssetChange(Id(1), ContentAssetChangeKind.Updated) },
            ContentManifestDiff.Compare(Manifest(original), Manifest(updated)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compare_DuplicateAssetIds_RejectsAmbiguousManifest(bool inPrevious)
    {
        ContentManifest duplicate = Manifest(Entry(1), Entry(1, "2"));

        Assert.Throws<InvalidDataException>(() => ContentManifestDiff.Compare(
            inPrevious ? duplicate : Manifest(), inPrevious ? Manifest() : duplicate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compare_UnsupportedSchema_RejectsManifest(bool inPrevious)
    {
        var unsupported = new ContentManifest(99, []);

        Assert.Throws<InvalidDataException>(() => ContentManifestDiff.Compare(
            inPrevious ? unsupported : Manifest(), inPrevious ? Manifest() : unsupported));
    }

    [Fact]
    public void Compare_NullCurrent_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => ContentManifestDiff.Compare(null, null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compare_NullEntry_RejectsManifest(bool inPrevious)
    {
        ContentManifest invalid = Manifest([null!]);

        Assert.Throws<InvalidDataException>(() => ContentManifestDiff.Compare(
            inPrevious ? invalid : Manifest(), inPrevious ? Manifest() : invalid));
    }

    [Fact]
    public void Compare_NullEntryCollection_RejectsManifest()
    {
        var invalid = new ContentManifest(ContentManifest.CurrentSchemaVersion, null!);

        Assert.Throws<InvalidDataException>(() => ContentManifestDiff.Compare(null, invalid));
    }

    [Fact]
    public void Compare_NullDependencyCollection_RejectsManifest()
    {
        var invalid = new ContentManifestEntry(Id(1), "raw-json", "game/a", "settings", "source", "artifact", 2, null!);

        Assert.Throws<InvalidDataException>(() => ContentManifestDiff.Compare(null, Manifest(invalid)));
    }

    [Fact]
    public void ChangeRecord_ContainsNoManagedReferences()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ContentAssetChange>());
    }

    [Fact]
    public void Compare_GeneratedManifests_MatchesCanonicalEntryDifferences()
    {
        var random = new Random(108);
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var previous = new List<ContentManifestEntry>();
            var current = new List<ContentManifestEntry>();
            var expected = new List<ContentAssetChange>();
            for (int id = 1; id <= 16; id++)
            {
                ContentManifestEntry before = Entry(id, "1", Id(20), Id(19));
                ContentManifestEntry after = Entry(id, random.Next(2) == 0 ? "1" : "2", Id(19), Id(20));
                bool wasPresent = random.Next(2) == 0;
                bool isPresent = random.Next(2) == 0;
                if (wasPresent) previous.Add(before);
                if (isPresent) current.Add(after);
                if (wasPresent && !isPresent) expected.Add(new(Id(id), ContentAssetChangeKind.Removed));
                else if (!wasPresent && isPresent) expected.Add(new(Id(id), ContentAssetChangeKind.Added));
                else if (wasPresent && Manifest(before).ToCanonicalJson() != Manifest(after).ToCanonicalJson())
                    expected.Add(new(Id(id), ContentAssetChangeKind.Updated));
            }
            random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(previous));
            random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(current));

            Assert.Equal(expected, ContentManifestDiff.Compare(Manifest(previous.ToArray()), Manifest(current.ToArray())));
        }
    }
}
