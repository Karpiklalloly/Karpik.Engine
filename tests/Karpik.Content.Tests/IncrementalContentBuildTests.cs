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

    private sealed class BuildFixture : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();
        public string Source { get; }
        public string Output { get; }
        public ProbeProcessor Processor { get; } = new();
        public ContentBuildOptions Options { get; }

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

        public void AssertMatchesClean(ContentBuildResult incremental)
        {
            string cleanOutput = _directory.CreateSubdirectory("clean");
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
