using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class ModContentCaptureTests
{
    [Theory]
    [InlineData(AssetTarget.Client)]
    [InlineData(AssetTarget.Server)]
    public async Task CaptureUsesOnlySelectedSideAndRetainsUnreadBytesAfterSourceDeletion(AssetTarget side)
    {
        using var fixture = new PackageFixture();
        string output = fixture.Build(side);
        string other = fixture.Side(side == AssetTarget.Client ? AssetTarget.Server : AssetTarget.Client);
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "overrides.json"), "unreadable other side descriptor");
        string uncooked = Path.Combine(fixture.GenerationRoot(side), "artifacts/not-listed.bin");
        File.WriteAllText(uncooked, "not copied");
        File.Delete(Path.Combine(output, ".karpik-generations.gate"));
        var originalFiles = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Order().ToArray();
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        ModContentCapture? capture;
        try { capture = await fixture.Capture(side); }
        finally
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        using var captured = Assert.IsType<ModContentCapture>(capture);
        Assert.Equal(originalFiles, Directory.GetFiles(output, "*", SearchOption.AllDirectories).Order());
        Assert.Equal(output, captured.Package.SourcePath);
        Assert.Equal("patch", captured.Package.ModId);
        var entry = Assert.Single(captured.Package.Manifest.Entries);
        Assert.Equal(fixture.Asset, entry.AssetId);
        Assert.Empty(captured.Package.Overrides);
        Assert.Throws<FileNotFoundException>(() => captured.Package.Store.Get("artifacts/not-listed.bin"));
        var lifetime = Assert.IsAssignableFrom<IContentStoreLifetime>(captured.Package.Store);
        lifetime.Retain();
        try
        {
            Directory.Delete(fixture.Mod, recursive: true);
            captured.Dispose();
            Assert.Equal("{\"value\":1}", Encoding.UTF8.GetString((await captured.Package.Store.GetAsync(entry.ArtifactLocator)).Span));
        }
        finally { lifetime.Release(); }
        Assert.Throws<ObjectDisposedException>(() => captured.Package.Store.Get(entry.ArtifactLocator));
    }

    [Fact]
    public async Task AbsentSideIsNotAPackageButExistingIncompleteSideIsRejected()
    {
        using var fixture = new PackageFixture();
        fixture.Build(AssetTarget.Client);
        Assert.Null(await fixture.Capture(AssetTarget.Server));
        Directory.CreateDirectory(fixture.Side(AssetTarget.Server));
        Assert.Contains("generation", (await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Capture(AssetTarget.Server))).Message);
    }

    [Theory]
    [InlineData("wrong-namespace", AssetTarget.Client)]
    [InlineData("patch", AssetTarget.Server)]
    public async Task RejectsWrongGenerationNamespaceOrTarget(string buildNamespace, AssetTarget buildTarget)
    {
        using var fixture = new PackageFixture();
        fixture.Build(AssetTarget.Client, buildNamespace, buildTarget);
        Assert.Contains("namespace or target", (await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Capture(AssetTarget.Client))).Message);
    }

    [Fact]
    public async Task InvalidOverrideRecordsReportAndDoNotSuppressValidSiblings()
    {
        using var fixture = new PackageFixture();
        string output = fixture.Build(AssetTarget.Client);
        AssetId game = AssetId.New(), external = AssetId.New();
        string declaration = Path.Combine(output, "overrides.json");
        File.WriteAllText(declaration, $$"""
            {"schemaVersion":1,"generationId":"{{fixture.GenerationId(AssetTarget.Client)}}","overrides":[
              {"targetModId":null,"targetAssetId":"{{game}}","replacementAssetId":"{{fixture.Asset}}"},
              {"targetModId":null,"targetAssetId":"bad-id","replacementAssetId":"{{fixture.Asset}}"},
              {"targetModId":7,"targetAssetId":"{{external}}","replacementAssetId":"{{fixture.Asset}}"},
              {"targetAssetId":"{{external}}","replacementAssetId":"{{fixture.Asset}}"},
              42,
              {"targetModId":"other.mod","targetAssetId":"{{external}}","replacementAssetId":"{{fixture.Asset}}"}
            ]}
            """);
        List<ContentDiagnostic> diagnostics = [];
        using var captured = Assert.IsType<ModContentCapture>(await fixture.Capture(AssetTarget.Client, report: diagnostics.Add));
        Assert.Equal([new ModContentOverride(null, game, fixture.Asset), new ModContentOverride("other.mod", external, fixture.Asset)], captured.Package.Overrides);
        Assert.Equal(4, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(declaration, d.RelativePath));
        Assert.Contains("bad-id", diagnostics[0].Message);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":2,\"generationId\":\"@gid@\",\"overrides\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"generationId\":\"@gid@\",\"overrides\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"generationId\":\"00000000000000000000000000000000\",\"overrides\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"generationId\":\"@gid@\",\"overrides\":[]}")]
    public async Task MalformedDeclarationContainerRejectsPackage(string json)
    {
        using var fixture = new PackageFixture();
        string output = fixture.Build(AssetTarget.Client);
        File.WriteAllText(Path.Combine(output, "overrides.json"), json.Replace("@gid@", fixture.GenerationId(AssetTarget.Client)));
        Assert.Contains("overrides.json", (await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Capture(AssetTarget.Client))).Message);
    }

    [Fact]
    public async Task ReuseRequiresMatchingSourceGenerationAndExactDeclarationBytesAndTakesIndependentClaim()
    {
        using var fixture = new PackageFixture();
        string output = fixture.Build(AssetTarget.Client);
        string declaration = Path.Combine(output, "overrides.json");
        File.WriteAllText(declaration, fixture.Declaration());
        using var first = Assert.IsType<ModContentCapture>(await fixture.Capture(AssetTarget.Client));
        using var reused = Assert.IsType<ModContentCapture>(await fixture.Capture(AssetTarget.Client, first.Package));
        Assert.Same(first.Package.Store, reused.Package.Store);
        first.Dispose();
        Assert.NotEmpty(reused.Package.Store.Get(reused.Package.Manifest.Entries[0].ArtifactLocator).ToArray());

        File.AppendAllText(declaration, "\n");
        using var changedDeclaration = Assert.IsType<ModContentCapture>(await fixture.Capture(AssetTarget.Client, reused.Package));
        Assert.NotSame(reused.Package.Store, changedDeclaration.Package.Store);
        fixture.Build(AssetTarget.Client, payload: "{\"value\":2}");
        File.WriteAllText(declaration, fixture.Declaration());
        using var changedGeneration = Assert.IsType<ModContentCapture>(await fixture.Capture(AssetTarget.Client, changedDeclaration.Package));
        Assert.NotSame(changedDeclaration.Package.Store, changedGeneration.Package.Store);

        string moved = fixture.Mod + "-copied";
        Directory.Move(fixture.Mod, moved);
        using var differentSource = Assert.IsType<ModContentCapture>(await ModContentCapture.CaptureAsync(moved, "patch", AssetTarget.Client, changedGeneration.Package));
        Assert.NotSame(changedGeneration.Package.Store, differentSource.Package.Store);
    }

    [Theory]
    [InlineData("overrides.json")]
    [InlineData("manifest.json")]
    [InlineData(".karpik-current-generation")]
    public async Task CaptureRejectsMetadataChangedBetweenSnapshotAndPublication(string changedFile)
    {
        using var fixture = new PackageFixture();
        string side = fixture.Build(AssetTarget.Client);
        File.WriteAllText(Path.Combine(side, "overrides.json"), fixture.Declaration("[42]"));
        Assert.Contains("changed during capture", (await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Capture(AssetTarget.Client,
            report: _ => File.AppendAllText(Path.Combine(side, changedFile), "\n")))).Message);
    }

    [Fact]
    public async Task ReuseStillRejectsMissingCookedArtifact()
    {
        using var fixture = new PackageFixture();
        fixture.Build(AssetTarget.Client);
        using var first = Assert.IsType<ModContentCapture>(await fixture.Capture(AssetTarget.Client));
        File.Delete(Path.Combine(fixture.GenerationRoot(AssetTarget.Client), first.Package.Manifest.Entries[0].ArtifactLocator));
        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Capture(AssetTarget.Client, first.Package));
    }

    [Fact]
    public async Task CapturedManifestEntryAndDependencyListsAreReadOnly()
    {
        using var fixture = new PackageFixture();
        fixture.Build(AssetTarget.Client);
        using var captured = Assert.IsType<ModContentCapture>(await fixture.Capture(AssetTarget.Client));
        var entries = Assert.IsAssignableFrom<IList<ContentManifestEntry>>(captured.Package.Manifest.Entries);
        Assert.True(entries.IsReadOnly);
        var dependencies = Assert.IsAssignableFrom<IList<AssetId>>(Assert.Single(entries).Dependencies);
        Assert.Throws<NotSupportedException>(() => dependencies.Add(AssetId.New()));
    }

    [Theory]
    [InlineData("overrides.json")]
    [InlineData(".karpik-current-generation")]
    [InlineData("artifact")]
    public async Task FifoIsRejectedWithoutBlockingOpen(string selectedFile)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new PackageFixture();
        string side = fixture.Build(AssetTarget.Client);
        string path = selectedFile == "artifact" ? fixture.Artifact() : Path.Combine(side, selectedFile);
        File.Delete(path);
        Assert.Equal(0, MkFifo(path, 0x180)); // 0600
        var capture = fixture.Capture(AssetTarget.Client);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => capture.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("ordinary Content file", error.Message);
        }
        finally
        {
            if (!capture.IsCompleted)
            {
                // A regression may wait in open(2). Bring it back even when the assertion fails.
                using var release = new SafeFileHandle(OpenLinux(path, 0x802), ownsHandle: true); // O_RDWR | O_NONBLOCK
                try { (await capture.WaitAsync(TimeSpan.FromSeconds(2)))?.Dispose(); }
                catch (Exception) { /* The assertion above reports the original failure. */ }
            }
        }
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("artifact-parent")]
    [InlineData("broken-declaration")]
    public async Task MalformedLinksIncludingParentDirectoriesAreRejected(string linkedPath)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new PackageFixture();
        string side = fixture.Build(AssetTarget.Client);
        if (linkedPath == "broken-declaration")
            File.CreateSymbolicLink(Path.Combine(side, "overrides.json"), side + "-missing");
        else
        {
            string directory = linkedPath == "directory" ? Path.GetDirectoryName(side)! : Path.GetDirectoryName(fixture.Artifact())!;
            Directory.Move(directory, directory + "-real");
            Directory.CreateSymbolicLink(directory, directory + "-real");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Capture(AssetTarget.Client));
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(5, 4)]
    public async Task CopyRejectsSizeChangeAndNeverWritesPastReservation(int actual, int declared)
    {
        using var from = new MemoryStream(new byte[actual]);
        using var to = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => CopyArtifactAsync(null, from, to, declared, CancellationToken.None));
        Assert.Equal(Math.Min(actual, declared), to.Length);
    }

    [Fact]
    public async Task GlobalQuotaAllowsExactLimitButCountsRetainedStoresUntilLastRelease()
    {
        const long limit = 4L * 1024 * 1024 * 1024;
        using var existing = new PackageFixture();
        existing.Build(AssetTarget.Client);
        using var active = Assert.IsType<ModContentCapture>(await existing.Capture(AssetTarget.Client));
        var retained = Assert.IsAssignableFrom<IContentStoreLifetime>(active.Package.Store);
        retained.Retain();
        active.Dispose();
        try
        {
            using var candidate = new PackageFixture();
            candidate.Build(AssetTarget.Client);
            string artifact = candidate.Artifact();
            File.Delete(artifact);
            candidate.RewriteManifest(limit);
            Assert.Contains("staging budget", (await Assert.ThrowsAsync<InvalidDataException>(() => candidate.Capture(AssetTarget.Client))).Message);
            candidate.RewriteManifest(limit - active.Package.Manifest.Entries[0].Size);
            await Assert.ThrowsAsync<FileNotFoundException>(() => candidate.Capture(AssetTarget.Client));
        }
        finally { retained.Release(); }
        using var final = new PackageFixture();
        final.Build(AssetTarget.Client);
        File.Delete(final.Artifact());
        final.RewriteManifest(limit);
        await Assert.ThrowsAsync<FileNotFoundException>(() => final.Capture(AssetTarget.Client));
        final.RewriteManifest(limit + 1);
        Assert.Contains("staging budget", (await Assert.ThrowsAsync<InvalidDataException>(() => final.Capture(AssetTarget.Client))).Message);
    }

    [Theory]
    [InlineData("overrides.json")]
    [InlineData("manifest.json")]
    public async Task FourMiBMetadataIsAllowedAndOneMoreByteRejectsCapture(string name)
    {
        using var fixture = new PackageFixture();
        string side = fixture.Build(AssetTarget.Client);
        string path = Path.Combine(side, name);
        if (name == "overrides.json") File.WriteAllText(path, fixture.Declaration());
        int bytes = (int)new FileInfo(path).Length;
        File.AppendAllText(path, new string(' ', 4 * 1024 * 1024 - bytes));
        using var capture = Assert.IsType<ModContentCapture>(await fixture.Capture(AssetTarget.Client));
        File.AppendAllText(path, " ");
        Assert.Contains("exceeds 4194304 bytes", (await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Capture(AssetTarget.Client))).Message);
    }

    [Fact]
    public async Task CaptureHonorsCancellationAndDoesNotDeleteOtherOwnedDirectories()
    {
        using var fixture = new PackageFixture();
        string side = fixture.Build(AssetTarget.Client);
        string declaration = Path.Combine(side, "overrides.json");
        File.WriteAllText(declaration, fixture.Declaration("[42]"));
        string other = Directory.CreateTempSubdirectory("karpik-mod-content-").FullName;
        File.WriteAllText(Path.Combine(other, "keep.bin"), "foreign");
        try
        {
            using var cancel = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Capture(AssetTarget.Client, report: _ => cancel.Cancel(), ct: cancel.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Capture(AssetTarget.Client, ct: cancel.Token));
            using (Assert.IsType<ModContentCapture>(await fixture.Capture(AssetTarget.Client))) { }
            Assert.Equal("foreign", File.ReadAllText(Path.Combine(other, "keep.bin")));
        }
        finally { Directory.Delete(other, recursive: true); }
    }

    [Theory]
    [InlineData(true, "{}")]
    [InlineData(false, "{}")]
    [InlineData(true, "null")]
    [InlineData(false, "null")]
    [InlineData(true, "{},\"dependencies\":[]")]
    [InlineData(false, "{},\"dependencies\":[]")]
    [InlineData(true, "[\"not-a-guid\"],\"dependencies\":[]")]
    [InlineData(false, "[\"not-a-guid\"],\"dependencies\":[]")]
    [InlineData(true, "[],\"dependencies\":[]")]
    [InlineData(false, "[],\"dependencies\":[]")]
    public async Task ModCaptureRejectsMalformedDependenciesBeforeCanonicalComparison(bool immutable, string dependencies)
    {
        using var fixture = new PackageFixture();
        const AssetTarget side = AssetTarget.Client;
        string publication = fixture.Build(side);
        string path = Path.Combine(immutable ? fixture.GenerationRoot(side) : publication, "manifest.json");
        string canonical = ContentManifest.LoadFromFile(path).ToCanonicalJson();
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        document["entries"]!.AsArray()[0]!["dependencies"] = "r4-marker";
        // Substitute raw JSON so a duplicate field can also be exercised.
        File.WriteAllText(path, document.ToJsonString().Replace("\"r4-marker\"", dependencies, StringComparison.Ordinal));

        // Leave the descriptor hash untouched: the ordinary manifest parser still canonicalizes this as empty.
        Assert.Equal(canonical, ContentManifest.LoadFromFile(path).ToCanonicalJson());
        Assert.Contains("dependencies", (await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Capture(side))).Message);
    }

    [Fact]
    public async Task UnsafeManifestLocatorCannotEscapeArtifacts()
    {
        using var fixture = new PackageFixture();
        fixture.Build(AssetTarget.Client);
        fixture.RewriteManifest(locator: "artifacts/../../outside.cooked");
        Assert.Contains("Unsafe cooked artifact locator", (await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Capture(AssetTarget.Client))).Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task OnDiskSizeMustExactlyMatchManifest(int delta)
    {
        using var fixture = new PackageFixture();
        fixture.Build(AssetTarget.Client);
        string path = fixture.Artifact();
        using (var file = File.OpenWrite(path)) file.SetLength(file.Length + delta);
        Assert.Contains("size mismatch", (await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Capture(AssetTarget.Client))).Message);
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "CopyArtifactAsync")]
    private static extern Task CopyArtifactAsync(ModContentCapture? owner, Stream source, Stream destination, long size, CancellationToken ct);

    [DllImport("libc", EntryPoint = "mkfifo")]
    private static extern int MkFifo(string path, uint mode);

    [DllImport("libc", EntryPoint = "open")]
    private static extern nint OpenLinux(string path, int flags);

    private sealed class PackageFixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("karpik-mod-fixture-").FullName;
        internal string Mod => Path.Combine(_root, "Mods", "patch");
        internal AssetId Asset { get; } = AssetId.New();
        internal string Side(AssetTarget side) => Path.Combine(Mod, "Content", side.ToString());
        internal string GenerationId(AssetTarget side) => File.ReadAllText(Path.Combine(Side(side), ".karpik-current-generation")).Trim();
        internal string GenerationRoot(AssetTarget side) => Path.Combine(Side(side), ".generations", GenerationId(side));
        internal string Artifact() => Path.Combine(GenerationRoot(AssetTarget.Client), ContentManifest.LoadFromFile(
            Path.Combine(GenerationRoot(AssetTarget.Client), "manifest.json")).Entries[0].ArtifactLocator);
        internal Task<ModContentCapture?> Capture(AssetTarget side, ModContentPackage? previous = null,
            Action<ContentDiagnostic>? report = null, CancellationToken ct = default) => ModContentCapture.CaptureAsync(Mod, "patch", side, previous, report, ct);

        internal void RewriteManifest(long? size = null, string? locator = null)
        {
            string generation = GenerationRoot(AssetTarget.Client);
            string path = Path.Combine(generation, "manifest.json");
            var manifest = ContentManifest.LoadFromFile(path);
            var entry = Assert.Single(manifest.Entries);
            manifest = new ContentManifest(manifest.SchemaVersion, [new ContentManifestEntry(entry.AssetId, entry.DeclaredType, entry.LogicalName,
                entry.ImportSettingsHash, entry.SourceHash, locator ?? entry.ArtifactLocator, size ?? entry.Size, entry.Dependencies)]);
            manifest.SaveToFile(path);
            manifest.SaveToFile(Path.Combine(Side(AssetTarget.Client), "manifest.json"));
            string descriptorPath = Path.Combine(generation, "generation.json");
            var descriptor = JsonNode.Parse(File.ReadAllText(descriptorPath))!.AsObject();
            descriptor["ManifestHash"] = ContentHashing.HashString(manifest.ToCanonicalJson());
            File.WriteAllText(descriptorPath, descriptor.ToJsonString());
        }

        internal string Declaration(string rules = "[]") =>
            $$"""{"schemaVersion":1,"generationId":"{{GenerationId(AssetTarget.Client)}}","overrides":{{rules}}}""";

        internal string Build(AssetTarget side, string buildNamespace = "patch", AssetTarget? buildTarget = null, string payload = "{\"value\":1}")
        {
            string source = Path.Combine(_root, "Source");
            Directory.CreateDirectory(source);
            string assetPath = Path.Combine(source, "asset.json");
            File.WriteAllText(assetPath, payload);
            File.WriteAllText(assetPath + ".meta", JsonSerializer.Serialize(new
            {
                schemaVersion = 1, assetId = Asset.ToString(), declaredType = "raw-json", logicalName = buildNamespace + "/asset",
                importSettings = new { }, dependencies = Array.Empty<string>()
            }));
            string output = Side(side);
            using var result = new ContentBuildCoordinator().Build(new ContentBuildOptions
            { SourceRoot = source, OutputRoot = output, Namespace = buildNamespace, Target = buildTarget ?? side });
            Assert.True(result.Success, string.Join('\n', result.Diagnostics));
            return output;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
