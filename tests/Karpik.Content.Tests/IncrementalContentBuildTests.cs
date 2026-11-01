using System.Text.Json;
using System.Text.Json.Nodes;
using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class IncrementalContentBuildTests
{
    private const string CacheFile = ".karpik-content-cache.v1.json";

    [Fact]
    public void Build_OneSourceChanges_ReusesOtherResultAcrossCoordinators()
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json");
        fixture.Add("b.json");
        ContentBuildResult initial = fixture.Build();
        ContentManifestEntry unchanged = initial.Manifest!.Entries.Single(e => e.LogicalName == "game/b");
        string unchangedPath = fixture.ArtifactPath(unchanged);
        var oldTime = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(unchangedPath, oldTime);
        File.WriteAllText(Path.Combine(fixture.Source, "a.json"), "{\"v\":2}");

        ContentBuildResult changed = fixture.Build();

        Assert.True(changed.Success);
        Assert.Equal(2, fixture.Processor.Calls["a.json"]);
        Assert.Equal(1, fixture.Processor.Calls["b.json"]);
        Assert.Equal(oldTime, File.GetLastWriteTimeUtc(unchangedPath));
        Assert.Equal("{}", File.ReadAllText(unchangedPath));
        fixture.AssertMatchesClean(changed);
    }

    [Fact]
    public void Build_UnchangedInputs_ReplaysProcessorWarnings()
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json");
        ContentBuildResult initial = fixture.Build();

        ContentBuildResult repeated = fixture.Build();

        Assert.Equal(initial.Diagnostics, repeated.Diagnostics);
        Assert.Single(repeated.Diagnostics);
        Assert.Equal(1, fixture.Processor.Calls["a.json"]);
    }

    [Fact]
    public void Build_SameSizeArtifactCorruption_ReprocessesAsset()
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json", "{\"v\":1}");
        ContentBuildResult initial = fixture.Build();
        string artifact = fixture.ArtifactPath(Assert.Single(initial.Manifest!.Entries));
        File.WriteAllText(artifact, "{\"v\":9}");

        ContentBuildResult repaired = fixture.Build();

        Assert.True(repaired.Success);
        Assert.Equal(2, fixture.Processor.Calls["a.json"]);
        Assert.Equal("{\"v\":1}", File.ReadAllText(artifact));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessorEmittedUnknownDependency_IsRejected(bool validateOnly)
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json", JsonSerializer.Serialize(new { reference = AssetId.New().ToString() }));

        ContentBuildResult result = validateOnly ? fixture.Validate() : fixture.Build();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.UnknownDependency);
        Assert.False(File.Exists(Path.Combine(fixture.Output, "manifest.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessorEmittedCycle_IsRejected(bool validateOnly)
    {
        using var fixture = new BuildFixture();
        AssetId a = AssetId.New();
        AssetId b = AssetId.New();
        fixture.Add("a.json", JsonSerializer.Serialize(new { reference = b.ToString() }), a);
        fixture.Add("b.json", JsonSerializer.Serialize(new { reference = a.ToString() }), b);

        ContentBuildResult result = validateOnly ? fixture.Validate() : fixture.Build();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.DependencyCycle);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("json")]
    [InlineData("schema")]
    [InlineData("manifestHash")]
    [InlineData("namespace")]
    [InlineData("target")]
    [InlineData("duplicate")]
    [InlineData("nullRecord")]
    [InlineData("nullDiagnostics")]
    [InlineData("errorDiagnostic")]
    public void Build_UnusableIndex_FallsBackToProcessing(string damage)
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json");
        Assert.True(fixture.Build().Success);
        JsonObject index = JsonNode.Parse(File.ReadAllText(fixture.CachePath))!.AsObject();
        JsonArray records = index["records"]!.AsArray();
        switch (damage)
        {
            case "missing": File.Delete(fixture.CachePath); break;
            case "json": File.WriteAllText(fixture.CachePath, "{"); break;
            case "schema": index["schemaVersion"] = 99; break;
            case "manifestHash": index["manifestHash"] = new string('0', 64); break;
            case "namespace": index["namespace"] = "other"; break;
            case "target": index["target"] = (int)AssetTarget.Server; break;
            case "duplicate": records.Add(records[0]!.DeepClone()); break;
            case "nullRecord": records[0] = null; break;
            case "nullDiagnostics": records[0]!["diagnostics"] = null; break;
            case "errorDiagnostic": records[0]!["diagnostics"]![0]!["severity"] = (int)ContentDiagnosticSeverity.Error; break;
        }
        if (damage is not ("missing" or "json")) File.WriteAllText(fixture.CachePath, index.ToJsonString());

        ContentBuildResult result = fixture.Build();

        Assert.True(result.Success);
        Assert.Equal(2, fixture.Processor.Calls["a.json"]);
        Assert.Single(result.Diagnostics);
        fixture.AssertMatchesClean(result);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("artifacts/../../outside")]
    [InlineData("/tmp/outside")]
    [InlineData("artifacts\\outside.cooked")]
    public void Build_InvalidCachedLocator_IsIgnored(string locator)
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json");
        Assert.True(fixture.Build().Success);
        string manifestPath = Path.Combine(fixture.Output, "manifest.json");
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["entries"]![0]!["artifactLocator"] = locator;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        JsonNode index = JsonNode.Parse(File.ReadAllText(fixture.CachePath))!;
        index["manifestHash"] = ContentHashing.HashString(ContentManifest.Parse(manifest.ToJsonString()).ToCanonicalJson());
        File.WriteAllText(fixture.CachePath, index.ToJsonString());

        ContentBuildResult result = fixture.Build();

        Assert.True(result.Success);
        Assert.Equal(2, fixture.Processor.Calls["a.json"]);
        fixture.AssertMatchesClean(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_MissingOrTruncatedArtifact_OnlyReprocessesDamagedAsset(bool truncated)
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json");
        fixture.Add("b.json");
        ContentBuildResult initial = fixture.Build();
        string artifact = fixture.ArtifactPath(initial.Manifest!.Entries.Single(e => e.LogicalName == "game/a"));
        if (truncated) File.WriteAllText(artifact, "");
        else File.Delete(artifact);

        ContentBuildResult result = fixture.Build();

        Assert.True(result.Success);
        Assert.Equal(2, fixture.Processor.Calls["a.json"]);
        Assert.Equal(1, fixture.Processor.Calls["b.json"]);
        fixture.AssertMatchesClean(result);
    }

    [Fact]
    public void Build_VerifiedNoOp_DoesNotCreateStagingOrRewritePublishedFiles()
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json");
        ContentBuildResult first = fixture.Build();
        string staging = Path.Combine(fixture.Output, ".staging");
        Directory.Delete(staging, recursive: true);
        var timestamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string[] paths = Directory.GetFiles(fixture.Output, "*", SearchOption.AllDirectories);
        foreach (string path in paths) File.SetLastWriteTimeUtc(path, timestamp);

        ContentBuildResult repeated = fixture.Build();

        Assert.True(repeated.Success);
        Assert.False(Directory.Exists(staging));
        Assert.Equal(1, fixture.Processor.Calls["a.json"]);
        Assert.Equal(first.Diagnostics, repeated.Diagnostics);
        Assert.All(paths, path => Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path)));
    }

    [Theory]
    [InlineData("importSettings")]
    [InlineData("logicalName")]
    [InlineData("version")]
    [InlineData("target")]
    [InlineData("path")]
    public void Build_ChangedProcessingInput_InvalidatesResult(string change)
    {
        using var fixture = new BuildFixture();
        AssetId id = fixture.Add("a.json");
        ContentBuildResult first = fixture.Build();
        ContentBuildOptions options = fixture.Options;
        if (change is "importSettings" or "logicalName")
        {
            string metaPath = Path.Combine(fixture.Source, "a.json.meta");
            JsonNode meta = JsonNode.Parse(File.ReadAllText(metaPath))!;
            if (change == "logicalName") meta[change] = "game/renamed";
            else meta[change] = new JsonObject { ["quality"] = "high" };
            File.WriteAllText(metaPath, meta.ToJsonString());
        }
        else if (change == "version") fixture.Processor.Version = "2";
        else if (change == "target") options = new ContentBuildOptions
        {
            SourceRoot = fixture.Source, OutputRoot = fixture.Output, Namespace = "game", Target = AssetTarget.Server
        };
        else
        {
            File.Move(Path.Combine(fixture.Source, "a.json"), Path.Combine(fixture.Source, "b.json"));
            File.Move(Path.Combine(fixture.Source, "a.json.meta"), Path.Combine(fixture.Source, "b.json.meta"));
        }

        ContentBuildResult result = fixture.Build(options);

        Assert.True(result.Success);
        Assert.Equal(2, fixture.Processor.Calls.Values.Sum());
        Assert.Equal(id, Assert.Single(result.Manifest!.Entries).AssetId);
        if (change == "target") Assert.NotEqual(first.Manifest!.ToCanonicalJson(), result.Manifest.ToCanonicalJson());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("serverOnly")]
    [InlineData("clientOnly")]
    public void Build_CachedProcessorDependency_IsRevalidatedAgainstCurrentSources(string change)
    {
        using var fixture = new BuildFixture();
        AssetId dependency = fixture.Add("b.json");
        fixture.Add("a.json", JsonSerializer.Serialize(new { reference = dependency.ToString() }));
        Assert.True(fixture.Build().Success);
        Dictionary<string, byte[]> before = fixture.PublishedBytes();
        string expectedCode;
        if (change == "missing")
        {
            File.Delete(Path.Combine(fixture.Source, "b.json"));
            File.Delete(Path.Combine(fixture.Source, "b.json.meta"));
            expectedCode = ContentDiagnosticCodes.UnknownDependency;
        }
        else
        {
            string metaPath = Path.Combine(fixture.Source, "b.json.meta");
            JsonNode meta = JsonNode.Parse(File.ReadAllText(metaPath))!;
            meta["targets"] = new JsonArray(change == "serverOnly" ? "Server" : "Client");
            File.WriteAllText(metaPath, meta.ToJsonString());
            expectedCode = change == "serverOnly" ? ContentDiagnosticCodes.TargetDependencyNotSelected : ContentDiagnosticCodes.SharedDependsOnSided;
        }

        ContentBuildResult result = fixture.Build();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == expectedCode);
        Assert.Equal(1, fixture.Processor.Calls["a.json"]);
        fixture.AssertPublishedBytes(before);
    }

    [Fact]
    public void Build_ReferenceContentsChange_DoesNotReprocessReferrer()
    {
        using var fixture = new BuildFixture();
        AssetId dependency = fixture.Add("b.json");
        fixture.Add("a.json", JsonSerializer.Serialize(new { reference = dependency.ToString() }), dependencies: [dependency]);
        Assert.True(fixture.Build().Success);
        File.WriteAllText(Path.Combine(fixture.Source, "b.json"), "{\"v\":2}");

        ContentBuildResult result = fixture.Build();

        Assert.True(result.Success);
        Assert.Equal(1, fixture.Processor.Calls["a.json"]);
        Assert.Equal(2, fixture.Processor.Calls["b.json"]);
        Assert.Equal(dependency, Assert.Single(result.Manifest!.Entries.Single(e => e.LogicalName == "game/a").Dependencies));
        fixture.AssertMatchesClean(result);
    }

    [Fact]
    public void Validate_CachedOutput_StillExecutesProcessors()
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json");
        Assert.True(fixture.Build().Success);

        Assert.True(fixture.Validate().Success);

        Assert.Equal(2, fixture.Processor.Calls["a.json"]);
    }

    [Fact]
    public void Build_ProcessorFailure_PreservesLastGoodOutputAndIndex()
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json");
        fixture.Add("b.json");
        Assert.True(fixture.Build().Success);
        Dictionary<string, byte[]> before = fixture.PublishedBytes();
        File.WriteAllText(Path.Combine(fixture.Source, "a.json"), "{");

        ContentBuildResult failed = fixture.Build();

        Assert.False(failed.Success);
        fixture.AssertPublishedBytes(before);
        File.WriteAllText(Path.Combine(fixture.Source, "a.json"), "{}");
        Assert.True(fixture.Build().Success);
        Assert.Equal(2, fixture.Processor.Calls["a.json"]); // Failed result did not replace the successful record.
        Assert.Equal(1, fixture.Processor.Calls["b.json"]);
    }

    [Fact]
    public void Build_IndexCannotBeSaved_PublishesUsableOutputAndLeavesNoTemporaryFile()
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json");
        Directory.CreateDirectory(fixture.CachePath); // Deterministic denial even when tests run as root.

        ContentBuildResult result = fixture.Build();

        Assert.True(result.Success);
        Assert.Equal("{}", File.ReadAllText(fixture.ArtifactPath(Assert.Single(result.Manifest!.Entries))));
        Assert.Empty(Directory.GetFiles(fixture.Output, CacheFile + ".tmp-*"));
        Assert.True(fixture.Build().Success);
        Assert.Equal(2, fixture.Processor.Calls["a.json"]);
    }

    [Fact]
    public void Build_OutputRootWithTrailingSeparator_StillReusesResults()
    {
        using var fixture = new BuildFixture();
        fixture.Add("a.json");
        var options = new ContentBuildOptions
        {
            SourceRoot = fixture.Source, OutputRoot = fixture.Output + Path.DirectorySeparatorChar,
            Namespace = "game", Target = AssetTarget.Client
        };

        Assert.True(fixture.Build(options).Success);
        Assert.True(fixture.Build(options).Success);

        Assert.Equal(1, fixture.Processor.Calls["a.json"]);
    }

    [Fact]
    public void Build_NewProcessorEdgeFormsCycleWithCachedEdge_RejectsPublication()
    {
        using var fixture = new BuildFixture();
        AssetId dependency = fixture.Add("b.json");
        AssetId referrer = fixture.Add("a.json", JsonSerializer.Serialize(new { reference = dependency.ToString() }));
        Assert.True(fixture.Build().Success);
        Dictionary<string, byte[]> before = fixture.PublishedBytes();
        File.WriteAllText(Path.Combine(fixture.Source, "b.json"), JsonSerializer.Serialize(new { reference = referrer.ToString() }));

        ContentBuildResult result = fixture.Build();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.DependencyCycle);
        Assert.Equal(1, fixture.Processor.Calls["a.json"]);
        fixture.AssertPublishedBytes(before);
    }

    [Fact]
    public void Build_EmptyAddDeleteAndMove_MatchesCleanOutput()
    {
        using var fixture = new BuildFixture();
        Assert.Empty(fixture.Build().Manifest!.Entries);
        Assert.Empty(fixture.Build().Manifest!.Entries);
        fixture.Add("a.json");
        fixture.AssertMatchesClean(fixture.Build());
        fixture.Add("b.json");
        fixture.AssertMatchesClean(fixture.Build());
        Assert.Equal(1, fixture.Processor.Calls["a.json"]);
        File.Move(Path.Combine(fixture.Source, "b.json"), Path.Combine(fixture.Source, "c.json"));
        File.Move(Path.Combine(fixture.Source, "b.json.meta"), Path.Combine(fixture.Source, "c.json.meta"));
        fixture.AssertMatchesClean(fixture.Build());
        File.Delete(Path.Combine(fixture.Source, "a.json"));
        File.Delete(Path.Combine(fixture.Source, "a.json.meta"));
        ContentBuildResult deleted = fixture.Build();
        Assert.Single(deleted.Manifest!.Entries);
        fixture.AssertMatchesClean(deleted);
        File.Delete(Path.Combine(fixture.Source, "c.json"));
        File.Delete(Path.Combine(fixture.Source, "c.json.meta"));
        ContentBuildResult empty = fixture.Build();
        Assert.Empty(empty.Manifest!.Entries);
        fixture.AssertMatchesClean(empty);
        Assert.Empty(Directory.GetFiles(fixture.Output, "*.cooked", SearchOption.AllDirectories));
    }

    private sealed class BuildFixture : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();
        public string Source { get; }
        public string Output { get; }
        public ProbeProcessor Processor { get; } = new();
        public ContentBuildOptions Options { get; }
        public string CachePath => Path.Combine(Output, CacheFile);

        public BuildFixture()
        {
            Source = _directory.CreateSubdirectory("source");
            Output = _directory.CreateSubdirectory("output");
            Options = new ContentBuildOptions { SourceRoot = Source, OutputRoot = Output, Namespace = "game", Target = AssetTarget.Client };
        }

        public AssetId Add(string path, string contents = "{}", AssetId? id = null, AssetTarget? targets = null,
            IEnumerable<AssetId>? dependencies = null) => AssetId.Parse(TestFixtures.CreateSourceFile(
                Source, path, contents, id, declaredType: "probe", targets: targets, dependencies: dependencies));

        public ContentBuildResult Build(ContentBuildOptions? options = null) =>
            new ContentBuildCoordinator([Processor]).Build(options ?? Options);

        public ContentBuildResult Validate() => new ContentBuildCoordinator([Processor]).Validate(Options);

        public string ArtifactPath(ContentManifestEntry entry) => Path.Combine(Output, entry.ArtifactLocator);

        public Dictionary<string, byte[]> PublishedBytes() => Directory.GetFiles(Output, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Output, path), File.ReadAllBytes);

        public void AssertPublishedBytes(Dictionary<string, byte[]> expected)
        {
            Dictionary<string, byte[]> actual = PublishedBytes();
            Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
            foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]);
        }

        public void AssertMatchesClean(ContentBuildResult incremental)
        {
            string cleanOutput = _directory.CreateSubdirectory("clean-" + Guid.NewGuid().ToString("N"));
            ContentBuildResult clean = new ContentBuildCoordinator([new ProbeProcessor { Version = Processor.Version }]).Build(
                new ContentBuildOptions { SourceRoot = Source, OutputRoot = cleanOutput, Namespace = "game", Target = Options.Target });
            Assert.True(clean.Success);
            Assert.Equal(clean.Manifest!.ToCanonicalJson(), incremental.Manifest!.ToCanonicalJson());
            Assert.Equal(clean.Diagnostics, incremental.Diagnostics);
            foreach (ContentManifestEntry entry in clean.Manifest.Entries)
                Assert.Equal(File.ReadAllBytes(Path.Combine(cleanOutput, entry.ArtifactLocator)), File.ReadAllBytes(ArtifactPath(entry)));
        }

        public void Dispose() => _directory.Dispose();
    }

    private sealed class ProbeProcessor : IContentProcessor
    {
        public Dictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);
        public string DeclaredType => "probe";
        public string Version { get; set; } = "1";

        public ContentProcessorResult Process(ReadOnlySpan<byte> sourceBytes, AssetMeta meta, string relativePath, ContentProcessorContext context)
        {
            Calls[relativePath] = Calls.GetValueOrDefault(relativePath) + 1;
            ContentProcessorResult parsed = new RawJsonProcessor().Process(sourceBytes, meta, relativePath, context);
            if (parsed.Diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.Error)) return parsed;
            using JsonDocument document = JsonDocument.Parse(parsed.CookedBytes);
            AssetId[] dependencies = document.RootElement.TryGetProperty("reference", out JsonElement reference)
                ? [AssetId.Parse(reference.GetString()!)] : [];
            return new ContentProcessorResult(parsed.CookedBytes, dependencies,
                [new ContentDiagnostic("PROBE001", ContentDiagnosticSeverity.Warning, relativePath, "Probe input validated.")]);
        }
    }
}
