using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class ContentGenerationTests
{
    [Fact]
    public void Build_PublishesImmutableGeneration_AndNoOpPreservesIdentity()
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.CreateSubdirectory("source");
        string output = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", "{\"value\":1}");
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" };
        var first = new ContentBuildCoordinator().Build(options);
        Assert.True(first.Success);
        string generation = Assert.Single(Directory.GetDirectories(Path.Combine(output, ".generations")));
        var entry = Assert.Single(first.Manifest!.Entries);
        string artifact = Path.Combine(generation, entry.ArtifactLocator);
        Assert.Equal("{\"value\":1}", File.ReadAllText(artifact));
        Assert.True(new ContentBuildCoordinator().Build(options).Success);
        Assert.Equal(generation, Assert.Single(Directory.GetDirectories(Path.Combine(output, ".generations"))));
    }

    [Fact]
    public void RetainedResult_ProtectsOldBytesAcrossBuilds_AndCollectsAfterRelease()
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.CreateSubdirectory("source");
        string output = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", "{\"value\":1}");
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game", RetainGeneration = true, Target = AssetTarget.Client };
        using var first = new ContentBuildCoordinator().Build(options);
        string oldRoot = first.PublishedGeneration!.RootPath;
        var oldEntry = Assert.Single(first.Manifest!.Entries);
        File.WriteAllText(Path.Combine(source, "a.json"), "{\"value\":2}");
        using var second = new ContentBuildCoordinator().Build(options);
        Assert.True(second.Success);
        Assert.NotEqual(first.GenerationId, second.GenerationId);
        Assert.Equal("game", second.PublishedGeneration!.Namespace);
        Assert.Equal(AssetTarget.Client, second.PublishedGeneration.Target);
        Assert.Equal("{\"value\":1}", File.ReadAllText(Path.Combine(oldRoot, oldEntry.ArtifactLocator)));
        using (var retry = ContentGeneration.Open(output, first.GenerationId!))
        {
            first.Dispose();
            Assert.True(Directory.Exists(oldRoot));
        }
        Assert.False(Directory.Exists(oldRoot));
        Assert.True(Directory.Exists(second.PublishedGeneration.RootPath));
    }

    [Fact]
    public void UnknownClaim_RetainsOldGeneration_AndStaleClaimsArePrunedWhileOwned()
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.CreateSubdirectory("source");
        string output = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", "{}");
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game", RetainGeneration = true };
        using var first = new ContentBuildCoordinator().Build(options);
        string oldRoot = first.PublishedGeneration!.RootPath;
        using (ContentGeneration.Open(output, first.GenerationId!)) { }
        Assert.Single(Directory.GetFiles(Path.Combine(oldRoot, ".claims")));
        string uncertain = Path.Combine(oldRoot, ".claims", "unknown");
        File.WriteAllText(uncertain, "cannot establish ownership");
        File.WriteAllText(Path.Combine(source, "a.json"), "{\"v\":2}");
        using var second = new ContentBuildCoordinator().Build(options);
        first.Dispose();
        Assert.True(Directory.Exists(oldRoot));
        File.Delete(uncertain);
        ContentGeneration.CollectUnused(output);
        Assert.False(Directory.Exists(oldRoot));
    }

    [Fact]
    public async Task ConcurrentBuilds_PublishCompleteGenerations_AndKeepRetainedResults()
    {
        using var temporary = new TemporaryDirectory();
        string sourceA = temporary.CreateSubdirectory("source-a");
        string sourceB = temporary.CreateSubdirectory("source-b");
        string output = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(sourceA, "a.json", "{\"a\":1}");
        TestFixtures.CreateSourceFile(sourceB, "b.json", "{\"b\":2}");
        var results = await Task.WhenAll(new[] { sourceA, sourceB }.Select(source => Task.Run(() =>
            new ContentBuildCoordinator().Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game", RetainGeneration = true }))));
        try
        {
            Assert.All(results, result => Assert.True(result.Success));
            Assert.All(results, result =>
            {
                var entry = Assert.Single(result.Manifest!.Entries);
                Assert.Equal(entry.Size, new FileInfo(Path.Combine(result.PublishedGeneration!.RootPath, entry.ArtifactLocator)).Length);
            });
            using var current = ContentGeneration.OpenCurrent(output);
            Assert.Contains(current.GenerationId, results.Select(result => result.GenerationId));
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Fact]
    public void InterruptedProjection_RecoversLastPublishedManifest_AndPrunesAbandonedCandidate()
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.CreateSubdirectory("source");
        string output = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", "{}");
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" };
        using var first = new ContentBuildCoordinator().Build(options);
        string pending = Path.Combine(output, ".generations", Guid.NewGuid().ToString("N") + ".pending");
        Directory.CreateDirectory(pending);
        File.WriteAllText(Path.Combine(pending, "incomplete"), "crashed before publication");
        File.WriteAllText(Path.Combine(output, "manifest.json"), "interrupted projection");
        using var recovered = new ContentBuildCoordinator().Build(options);
        Assert.True(recovered.Success);
        Assert.Equal(first.GenerationId, recovered.GenerationId);
        Assert.Equal(first.Manifest!.ToCanonicalJson(), ContentManifest.LoadFromFile(Path.Combine(output, "manifest.json")).ToCanonicalJson());
        Assert.False(Directory.Exists(pending));
    }

    [Fact]
    public void CorruptCurrentOrUnknownCandidateMetadata_FailsClosedDuringCollection()
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.CreateSubdirectory("source");
        string output = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", "{}");
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game", RetainGeneration = true };
        using var first = new ContentBuildCoordinator().Build(options);
        string oldRoot = first.PublishedGeneration!.RootPath;
        string oldDescriptor = Path.Combine(oldRoot, "generation.json");
        string validOld = File.ReadAllText(oldDescriptor);
        File.WriteAllText(Path.Combine(source, "a.json"), "{\"v\":2}");
        using var second = new ContentBuildCoordinator().Build(options);
        File.WriteAllText(oldDescriptor, "{\"SchemaVersion\":999}");
        first.Dispose();
        Assert.True(Directory.Exists(oldRoot));
        Assert.Equal(0, ContentGeneration.CollectUnused(output));
        File.WriteAllText(oldDescriptor, validOld);
        string currentDescriptor = Path.Combine(second.PublishedGeneration!.RootPath, "generation.json");
        string validCurrent = File.ReadAllText(currentDescriptor);
        File.WriteAllText(currentDescriptor, "{\"SchemaVersion\":999}");
        Assert.Equal(0, ContentGeneration.CollectUnused(output));
        Assert.True(Directory.Exists(oldRoot));
        File.WriteAllText(currentDescriptor, validCurrent);
        ContentGeneration.CollectUnused(output);
        Assert.False(Directory.Exists(oldRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagedCurrentArtifact_RebuildPublishesNewGenerationWithoutRewritingCapturedBytes(bool sameSizeCorruption)
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.CreateSubdirectory("source");
        string output = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", "{}");
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game", RetainGeneration = true };
        using var first = new ContentBuildCoordinator().Build(options);
        var entry = Assert.Single(first.Manifest!.Entries);
        string oldArtifact = Path.Combine(first.PublishedGeneration!.RootPath, entry.ArtifactLocator);
        if (sameSizeCorruption) File.WriteAllText(oldArtifact, "xx");
        else File.Delete(oldArtifact);
        using var repaired = new ContentBuildCoordinator().Build(options);
        Assert.True(repaired.Success);
        Assert.NotEqual(first.GenerationId, repaired.GenerationId);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(repaired.PublishedGeneration!.RootPath, entry.ArtifactLocator)));
        if (sameSizeCorruption) Assert.Equal("xx", File.ReadAllText(oldArtifact));
        else Assert.False(File.Exists(oldArtifact));
    }

    [Fact]
    public void LegacyImport_AnchorsOriginalBytesWithUnknownIdentity_BeforeValidatedBuild()
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.CreateSubdirectory("source");
        string output = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", "{}");
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" };
        using var original = new ContentBuildCoordinator().Build(options);
        // Model a pre-generation output while preserving the original public manifest/artifacts.
        Directory.Delete(Path.Combine(output, ".generations"), recursive: true);
        File.Delete(Path.Combine(output, ".karpik-current-generation"));
        using var imported = ContentGeneration.OpenOrImportCurrent(output);
        Assert.False(imported.IdentityKnown);
        Assert.Equal(string.Empty, imported.Namespace);
        Assert.Equal(AssetTarget.Shared, imported.Target);
        using var validated = new ContentBuildCoordinator().Build(options);
        Assert.True(validated.Success);
        Assert.NotEqual(imported.GenerationId, validated.GenerationId);
        using var current = ContentGeneration.OpenCurrent(output);
        Assert.True(current.IdentityKnown);
        Assert.Equal("game", current.Namespace);
        Assert.True(Directory.Exists(imported.RootPath));
    }

    [Fact]
    public void InvalidMarkerPath_FailsBeforeDestructiveLegacyMigration()
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.CreateSubdirectory("source");
        string output = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", "{}");
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" };
        using var original = new ContentBuildCoordinator().Build(options);
        string manifest = File.ReadAllText(Path.Combine(output, "manifest.json"));
        var entry = Assert.Single(original.Manifest!.Entries);
        Directory.Delete(Path.Combine(output, ".generations"), recursive: true);
        File.Delete(Path.Combine(output, ".karpik-current-generation"));
        Directory.CreateDirectory(Path.Combine(output, ".karpik-current-generation"));
        File.WriteAllText(Path.Combine(source, "a.json"), "{\"v\":2}");
        using var failed = new ContentBuildCoordinator().Build(options);
        Assert.False(failed.Success);
        Assert.Equal(manifest, File.ReadAllText(Path.Combine(output, "manifest.json")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(output, entry.ArtifactLocator)));
    }

    [Fact]
    public void EmptyTreeRequiresExplicitNamespace_AndValidEmptyManifestRemainsSupported()
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.CreateSubdirectory("source");
        string output = temporary.CreateSubdirectory("output");
        using var invalid = new ContentBuildCoordinator().Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output });
        Assert.False(invalid.Success);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == ContentDiagnosticCodes.NamespaceMismatch);
        Assert.False(File.Exists(Path.Combine(output, "manifest.json")));
        using var valid = new ContentBuildCoordinator().Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game", RetainGeneration = true });
        Assert.True(valid.Success);
        Assert.Empty(valid.PublishedGeneration!.Manifest.Entries);
        Assert.True(valid.PublishedGeneration.IdentityKnown);
    }

    [Fact]
    public void ValidNewerProjectionWithOldMarker_RecoversAnchorAndCollectsUnreferencedCompleteCandidate()
    {
        using var temporary = new TemporaryDirectory();
        string source = temporary.CreateSubdirectory("source");
        string output = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", "{\"v\":1}");
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game", RetainGeneration = true };
        using var first = new ContentBuildCoordinator().Build(options);
        var oldEntry = Assert.Single(first.Manifest!.Entries);
        string oldArtifact = Path.Combine(first.PublishedGeneration!.RootPath, oldEntry.ArtifactLocator);
        File.WriteAllText(Path.Combine(source, "a.json"), "{\"v\":2}");
        using var second = new ContentBuildCoordinator().Build(options);
        string secondRoot = second.PublishedGeneration!.RootPath;
        var newEntry = Assert.Single(second.Manifest!.Entries);
        string orphan = Path.Combine(output, ".generations", Guid.NewGuid().ToString("N"));
        // Model a complete unpublished candidate with no owners, distinct from an incomplete .pending directory.
        foreach (string path in Directory.GetFiles(secondRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(secondRoot, path);
            if (relative.StartsWith(".claims" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            string copy = Path.Combine(orphan, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(path, copy);
        }
        // Model death after valid projection B was installed, before switching authoritative marker A.
        File.WriteAllText(Path.Combine(output, ".karpik-current-generation"), first.GenerationId + "\n");
        Assert.Equal(second.Manifest.ToCanonicalJson(), ContentManifest.LoadFromFile(Path.Combine(output, "manifest.json")).ToCanonicalJson());
        Assert.Throws<InvalidDataException>(() => ContentGeneration.OpenCurrent(output));
        File.WriteAllText(Path.Combine(source, "a.json"), "{\"v\":1}");
        using var recovered = new ContentBuildCoordinator().Build(options);
        Assert.True(recovered.Success);
        Assert.Equal(first.Manifest.ToCanonicalJson(), recovered.Manifest!.ToCanonicalJson());
        Assert.Equal(first.Manifest.ToCanonicalJson(), ContentManifest.LoadFromFile(Path.Combine(output, "manifest.json")).ToCanonicalJson());
        Assert.Equal("{\"v\":1}", File.ReadAllText(oldArtifact));
        Assert.Equal("{\"v\":2}", File.ReadAllText(Path.Combine(secondRoot, newEntry.ArtifactLocator)));
        Assert.False(Directory.Exists(orphan));
        second.Dispose();
        Assert.False(Directory.Exists(secondRoot));
        Assert.True(Directory.Exists(first.PublishedGeneration.RootPath));
    }
}
