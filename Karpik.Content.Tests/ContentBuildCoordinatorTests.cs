using Karpik.Content.Core;
using System.Text.Json;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class ContentBuildCoordinatorTests
{
    [Fact]
    public void Build_UnchangedInputs_DoesNotRunProcessorAgain()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", "{}", logicalName: "game/a", declaredType: "counting");

        var processor = new CountingProcessor();
        var coordinator = new ContentBuildCoordinator([processor]);
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game", Target = AssetTarget.Client };

        Assert.True(coordinator.Build(options).Success);
        Assert.True(coordinator.Build(options).Success);
        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    public void Build_ValidTree_CreatesManifestAndArtifacts()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");

        string idA = TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", logicalName: "game/a");
        string idB = TestFixtures.CreateSourceFile(source, "b.json", """{"b":2}""", logicalName: "game/b");

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" });

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == ContentDiagnosticSeverity.Error));
        Assert.NotNull(result.Manifest);
        Assert.Equal(2, result.Manifest!.Entries.Count);

        string manifestPath = Path.Combine(output, "manifest.json");
        Assert.True(File.Exists(manifestPath));

        var manifest = ContentManifest.LoadFromFile(manifestPath);
        Assert.Equal(2, manifest.Entries.Count);
        // Entries ordered by AssetId
        Assert.True(manifest.Entries[0].AssetId.CompareTo(manifest.Entries[1].AssetId) < 0);

        foreach (var entry in manifest.Entries)
        {
            string artifactPath = Path.Combine(output, entry.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(artifactPath));
            Assert.Equal(entry.Size, new FileInfo(artifactPath).Length);
        }
    }

    [Fact]
    public void Build_CanonicalManifest_IsByteIdenticalRegardlessOfFileOrder()
    {
        using var tmp = new TemporaryDirectory();
        string source1 = tmp.CreateSubdirectory("source1");
        string source2 = tmp.CreateSubdirectory("source2");
        string output1 = tmp.CreateSubdirectory("output1");
        string output2 = tmp.CreateSubdirectory("output2");

        var guidA = Guid.NewGuid();
        var guidB = Guid.NewGuid();

        // Create same content but in different file order
        TestFixtures.CreateSourceFile(source1, "a.json", """{"a":1}""", id: new AssetId(guidA), logicalName: "game/a");
        TestFixtures.CreateSourceFile(source1, "b.json", """{"b":2}""", id: new AssetId(guidB), logicalName: "game/b");

        // Reverse order creation
        TestFixtures.CreateSourceFile(source2, "b.json", """{"b":2}""", id: new AssetId(guidB), logicalName: "game/b");
        TestFixtures.CreateSourceFile(source2, "a.json", """{"a":1}""", id: new AssetId(guidA), logicalName: "game/a");

        var coordinator = new ContentBuildCoordinator();
        var r1 = coordinator.Build(new ContentBuildOptions { SourceRoot = source1, OutputRoot = output1, Namespace = "game" });
        var r2 = coordinator.Build(new ContentBuildOptions { SourceRoot = source2, OutputRoot = output2, Namespace = "game" });

        Assert.True(r1.Success);
        Assert.True(r2.Success);

        string json1 = File.ReadAllText(Path.Combine(output1, "manifest.json"));
        string json2 = File.ReadAllText(Path.Combine(output2, "manifest.json"));
        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Build_ByteIdentical_DoesNotRewriteArtifacts()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");

        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", logicalName: "game/a");

        var coordinator = new ContentBuildCoordinator();
        var r1 = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" });
        Assert.True(r1.Success);

        string manifestPath = Path.Combine(output, "manifest.json");
        byte[] manifestBytes1 = File.ReadAllBytes(manifestPath);
        var artifactFile = Directory.EnumerateFiles(Path.Combine(output, "artifacts"), "*", SearchOption.AllDirectories).Single();
        DateTime lastWrite1 = File.GetLastWriteTimeUtc(artifactFile);
        byte[] artifactBytes1 = File.ReadAllBytes(artifactFile);

        // Wait a bit to ensure timestamp would differ if rewritten
        Thread.Sleep(1100);

        var r2 = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" });
        Assert.True(r2.Success, $"Second build failed: {string.Join("\n", r2.Diagnostics.Select(d => d.ToString()))} Exception: {r2.Diagnostics.FirstOrDefault()?.Message}");

        byte[] manifestBytes2 = File.ReadAllBytes(manifestPath);
        DateTime lastWrite2 = File.GetLastWriteTimeUtc(artifactFile);
        byte[] artifactBytes2 = File.ReadAllBytes(artifactFile);

        Assert.Equal(manifestBytes1, manifestBytes2);
        Assert.Equal(artifactBytes1, artifactBytes2);
        Assert.Equal(lastWrite1, lastWrite2);
    }

    [Fact]
    public void Build_FailedPreservesPreviousOutput()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");

        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", logicalName: "game/a");

        var coordinator = new ContentBuildCoordinator();
        var r1 = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" });
        Assert.True(r1.Success);
        byte[] manifestBefore = File.ReadAllBytes(Path.Combine(output, "manifest.json"));
        var artifactsBefore = Directory.EnumerateFiles(Path.Combine(output, "artifacts"), "*", SearchOption.AllDirectories).Select(p => (p, File.ReadAllBytes(p))).ToList();

        // Inject failing source
        string badGuid = Guid.NewGuid().ToString("D");
        File.WriteAllText(Path.Combine(source, "bad.json"), "{ invalid");
        File.WriteAllText(Path.Combine(source, "bad.json.meta"), "{\"schemaVersion\":1,\"assetId\":\"" + badGuid + "\",\"declaredType\":\"raw-json\",\"logicalName\":\"game/bad\",\"importSettings\":{}}");

        var r2 = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" });
        Assert.False(r2.Success);
        Assert.Contains(r2.Diagnostics, d => d.Code == ContentDiagnosticCodes.InvalidJsonContent);

        byte[] manifestAfter = File.ReadAllBytes(Path.Combine(output, "manifest.json"));
        Assert.Equal(manifestBefore, manifestAfter);

        var artifactsAfter = Directory.EnumerateFiles(Path.Combine(output, "artifacts"), "*", SearchOption.AllDirectories).Select(p => (p, File.ReadAllBytes(p))).ToList();
        Assert.Equal(artifactsBefore.Count, artifactsAfter.Count);
        for (int i = 0; i < artifactsBefore.Count; i++)
        {
            Assert.Equal(artifactsBefore[i].Item2, artifactsAfter[i].Item2);
        }

        // Cleanup bad
        File.Delete(Path.Combine(source, "bad.json"));
        File.Delete(Path.Combine(source, "bad.json.meta"));
    }

    [Fact]
    public void Validate_DetectsDuplicateAssetId()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        var guid = new AssetId(Guid.NewGuid());
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", id: guid, logicalName: "game/a");
        TestFixtures.CreateSourceFile(source, "b.json", """{"b":2}""", id: guid, logicalName: "game/b");

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game" });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.DuplicateAssetId);
    }

    [Fact]
    public void Validate_DetectsDuplicateLogicalName()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", logicalName: "game/dup");
        TestFixtures.CreateSourceFile(source, "b.json", """{"b":2}""", logicalName: "game/dup");

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game" });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.DuplicateLogicalName);
    }

    [Fact]
    public void Validate_DetectsInvalidJson()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        TestFixtures.CreateSourceFile(source, "a.json", """{ invalid }""", logicalName: "game/a");

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game" });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.InvalidJsonContent);
    }

    [Fact]
    public void Validate_DetectsMissingDependency()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        var missing = new AssetId(Guid.NewGuid());
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", logicalName: "game/a", dependencies: new[] { missing });

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game" });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.UnknownDependency);
    }

    [Fact]
    public void Validate_DetectsCycle()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        var idA = new AssetId(Guid.NewGuid());
        var idB = new AssetId(Guid.NewGuid());
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", id: idA, logicalName: "game/a", dependencies: new[] { idB });
        TestFixtures.CreateSourceFile(source, "b.json", """{"b":2}""", id: idB, logicalName: "game/b", dependencies: new[] { idA });

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game" });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.DependencyCycle);
    }

    [Fact]
    public void Validate_DetectsNamespaceMismatch()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", logicalName: "other/a");

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game" });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.NamespaceMismatch);
    }

    [Fact]
    public void Validate_DetectsUnsupportedType()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", logicalName: "game/a", declaredType: "unsupported-test");

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game" });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.UnsupportedDeclaredType);
    }

    [Fact]
    public void Validate_DetectsMissingMeta()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string full = Path.Combine(source, "a.json");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, """{"a":1}""");
        // No meta

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game" });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.MissingMetaFile);
    }

    [Fact]
    public void Build_MovePreservesAssetId()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");
        var guid = new AssetId(Guid.NewGuid());
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", id: guid, logicalName: "game/a");

        var coordinator = new ContentBuildCoordinator();
        var r1 = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" });
        Assert.True(r1.Success);
        string manifestJson1 = File.ReadAllText(Path.Combine(output, "manifest.json"));
        var manifest1 = ContentManifest.Parse(manifestJson1);
        Assert.Contains(manifest1.Entries, e => e.AssetId == guid);

        // Move file while keeping meta
        string oldPath = Path.Combine(source, "a.json");
        string oldMeta = oldPath + ".meta";
        string newPath = Path.Combine(source, "subdir", "moved.json");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.Move(oldPath, newPath);
        File.Move(oldMeta, newPath + ".meta");

        var r2 = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" });
        Assert.True(r2.Success, $"Second build after move failed: {string.Join("\n", r2.Diagnostics.Select(d => d.ToString()))}");
        string manifestJson2 = File.ReadAllText(Path.Combine(output, "manifest.json"));
        var manifest2 = ContentManifest.Parse(manifestJson2);
        Assert.Contains(manifest2.Entries, e => e.AssetId == guid);
        // Manifest should still have same AssetId, even though source path changed, hash will differ? Actually source bytes same, meta same, so artifact hash same, manifest entry same except sourceHash same. So should be identical except maybe? Let's ensure assetId unchanged
        Assert.Equal(manifest1.Entries.Single(e => e.AssetId == guid).AssetId, manifest2.Entries.Single(e => e.AssetId == guid).AssetId);
    }

    [Fact]
    public void Golden_ManifestAndCookedOutput()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");

        // Use deterministic GUIDs and content
        var guid = new AssetId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        string sourceContent = """{"z":3,"a":1}""";
        string expectedCooked = """{"a":1,"z":3}""";
        TestFixtures.CreateSourceFile(source, "a.json", sourceContent, id: guid, logicalName: "game/a", importSettings: new { b = 2, a = 1 });

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game" });
        Assert.True(result.Success);

        var manifest = ContentManifest.LoadFromFile(Path.Combine(output, "manifest.json"));
        var entry = manifest.Entries.Single();
        // Check importSettingsHash is deterministic regardless of order
        string canonicalImport = CanonicalJson.SerializeCanonical(System.Text.Json.JsonDocument.Parse("""{"a":1,"b":2}""").RootElement);
        string expectedImportHash = ContentHashing.HashString(canonicalImport);
        Assert.Equal(expectedImportHash, entry.ImportSettingsHash);

        string artifactPath = Path.Combine(output, entry.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar));
        string cooked = File.ReadAllText(artifactPath);
        Assert.Equal(expectedCooked, cooked);

        // Check sourceHash
        string expectedSourceHash = ContentHashing.HashString(sourceContent);
        Assert.Equal(expectedSourceHash, entry.SourceHash);
    }

    [Fact]
    public void Build_TargetFiltersAssets()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string clientOut = tmp.CreateSubdirectory("client-out");
        string serverOut = tmp.CreateSubdirectory("server-out");

        var sharedId = new AssetId(Guid.NewGuid());
        var clientId = new AssetId(Guid.NewGuid());
        var serverId = new AssetId(Guid.NewGuid());
        TestFixtures.CreateSourceFile(source, "shared.json", """{"s":1}""", id: sharedId, logicalName: "game/shared");
        TestFixtures.CreateSourceFile(source, "client.json", """{"c":1}""", id: clientId, logicalName: "game/client", targets: AssetTarget.Client, dependencies: new[] { sharedId });
        TestFixtures.CreateSourceFile(source, "server.json", """{"s":2}""", id: serverId, logicalName: "game/server", targets: AssetTarget.Server, dependencies: new[] { sharedId });

        var coordinator = new ContentBuildCoordinator();
        var clientResult = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = clientOut, Namespace = "game", Target = AssetTarget.Client });
        Assert.True(clientResult.Success, $"Client build failed: {string.Join("\n", clientResult.Diagnostics.Select(d => d.ToString()))}");
        Assert.Equal(2, clientResult.Manifest!.Entries.Count);
        Assert.Contains(clientResult.Manifest.Entries, e => e.AssetId == sharedId);
        Assert.Contains(clientResult.Manifest.Entries, e => e.AssetId == clientId);

        var serverResult = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = serverOut, Namespace = "game", Target = AssetTarget.Server });
        Assert.True(serverResult.Success, $"Server build failed: {string.Join("\n", serverResult.Diagnostics.Select(d => d.ToString()))}");
        Assert.Equal(2, serverResult.Manifest!.Entries.Count);
        Assert.Contains(serverResult.Manifest.Entries, e => e.AssetId == sharedId);
        Assert.Contains(serverResult.Manifest.Entries, e => e.AssetId == serverId);
    }

    [Fact]
    public void Validate_DuplicateLogicalName_AcrossIndependentTargets_Allowed()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        TestFixtures.CreateSourceFile(source, "client.json", """{"c":1}""", logicalName: "game/same", targets: AssetTarget.Client);
        TestFixtures.CreateSourceFile(source, "server.json", """{"s":1}""", logicalName: "game/same", targets: AssetTarget.Server);

        var coordinator = new ContentBuildCoordinator();
        var clientResult = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out-client"), Namespace = "game", Target = AssetTarget.Client });
        Assert.True(clientResult.Success, $"Client validate failed: {string.Join("\n", clientResult.Diagnostics.Select(d => d.ToString()))}");
        var serverResult = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out-server"), Namespace = "game", Target = AssetTarget.Server });
        Assert.True(serverResult.Success, $"Server validate failed: {string.Join("\n", serverResult.Diagnostics.Select(d => d.ToString()))}");
    }

    [Fact]
    public void Validate_SharedBuildTarget_WithSidedAssets_RequiresConcreteTarget()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        TestFixtures.CreateSourceFile(source, "client.json", """{"c":1}""", logicalName: "game/same", targets: AssetTarget.Client);
        TestFixtures.CreateSourceFile(source, "server.json", """{"s":1}""", logicalName: "game/same", targets: AssetTarget.Server);

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions
        {
            SourceRoot = source,
            OutputRoot = tmp.CreateSubdirectory("out"),
            Namespace = "game"
        });

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "KCO024");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.DuplicateLogicalName);
    }

    [Fact]
    public void Validate_DuplicateLogicalName_AcrossOverlappingTargets_Rejected()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        TestFixtures.CreateSourceFile(source, "shared.json", """{"s":1}""", logicalName: "game/same");
        TestFixtures.CreateSourceFile(source, "client.json", """{"c":1}""", logicalName: "game/same", targets: AssetTarget.Client);

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game", Target = AssetTarget.Client });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.DuplicateLogicalName);
    }

    [Fact]
    public void Validate_DependencyOnUnselectedTarget_Rejected()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        var serverId = new AssetId(Guid.NewGuid());
        TestFixtures.CreateSourceFile(source, "server.json", """{"s":1}""", id: serverId, logicalName: "game/server", targets: AssetTarget.Server);
        TestFixtures.CreateSourceFile(source, "client.json", """{"c":1}""", logicalName: "game/client", targets: AssetTarget.Client, dependencies: new[] { serverId });

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game", Target = AssetTarget.Client });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.TargetDependencyNotSelected);
    }

    [Fact]
    public void Validate_SharedDependsOnSided_Rejected()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        var clientId = new AssetId(Guid.NewGuid());
        TestFixtures.CreateSourceFile(source, "client.json", """{"c":1}""", id: clientId, logicalName: "game/client", targets: AssetTarget.Client);
        TestFixtures.CreateSourceFile(source, "shared.json", """{"s":1}""", logicalName: "game/shared", dependencies: new[] { clientId });

        var coordinator = new ContentBuildCoordinator();
        var result = coordinator.Validate(new ContentBuildOptions { SourceRoot = source, OutputRoot = tmp.CreateSubdirectory("out"), Namespace = "game", Target = AssetTarget.Client });
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.SharedDependsOnSided);
    }

    [Fact]
    public void Build_SameAsset_DifferentTargets_DifferentLocators()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string clientOut = tmp.CreateSubdirectory("client-out");
        string serverOut = tmp.CreateSubdirectory("server-out");

        var id = new AssetId(Guid.NewGuid());
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", id: id, logicalName: "game/a");

        var coordinator = new ContentBuildCoordinator();
        var clientResult = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = clientOut, Namespace = "game", Target = AssetTarget.Client });
        Assert.True(clientResult.Success, $"Client build failed: {string.Join("\n", clientResult.Diagnostics.Select(d => d.ToString()))}");
        var serverResult = coordinator.Build(new ContentBuildOptions { SourceRoot = source, OutputRoot = serverOut, Namespace = "game", Target = AssetTarget.Server });
        Assert.True(serverResult.Success, $"Server build failed: {string.Join("\n", serverResult.Diagnostics.Select(d => d.ToString()))}");

        var clientEntry = clientResult.Manifest!.Entries.Single();
        var serverEntry = serverResult.Manifest!.Entries.Single();
        Assert.Equal(clientEntry.AssetId, serverEntry.AssetId);
        Assert.Equal(clientEntry.SourceHash, serverEntry.SourceHash);
        Assert.NotEqual(clientEntry.ArtifactLocator, serverEntry.ArtifactLocator);

        byte[] clientCooked = File.ReadAllBytes(Path.Combine(clientOut, clientEntry.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar)));
        byte[] serverCooked = File.ReadAllBytes(Path.Combine(serverOut, serverEntry.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Equal(clientCooked, serverCooked);
    }

    private sealed class CountingProcessor : IContentProcessor
    {
        public int CallCount { get; private set; }
        public string DeclaredType => "counting";
        public string Version => "1";

        public ContentProcessorResult Process(ReadOnlySpan<byte> sourceBytes, AssetMeta meta, string relativePath, ContentProcessorContext context)
        {
            CallCount++;
            return new ContentProcessorResult(sourceBytes.ToArray(), [], []);
        }
    }
}
