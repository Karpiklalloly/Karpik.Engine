using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core.FileSystem;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks cooked content loading through the engine registry and store.</summary>
public sealed class ContentRuntimeTests
{
    /// <summary>Finds the repository root from the solution file.</summary>
    /// <returns>The absolute repository root path.</returns>
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "SSSuperGame.slnx")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Repo root not found.");
    }

    /// <summary>Verifies that the content manifest registers and resolves raw JSON.</summary>
    [Fact]
    public async Task Manifest_registers_and_raw_json_resolves()
    {
        string manifestPath = Path.Combine(RepoRoot(), "Source", "SSSuperGame.Client", "obj", "Debug", "net10.0", "Content", "manifest.json");
        Assert.True(File.Exists(manifestPath), "Build the Client project first.");
        string contentDir = Path.GetDirectoryName(manifestPath)!;

        var fs = new RemappedFileSystem(contentDir, contentDir);
        var store = new FileContentStore(fs);
        var registry = new ContentRegistry(fs, store);

        var diagnostics = new List<ContentDiagnostic>();
        ContentManifest manifest = ContentManifest.LoadFromFile(manifestPath, diagnostics);
        Assert.Empty(diagnostics);
        registry.RegisterManifest(manifest, store);

        var matchRef = new AssetRef<RawJsonPayload>("a80e8179-0861-41ca-a557-de8cfbeac225", 1);
        await registry.LoadAsync(matchRef, CancellationToken.None);
        bool alive = registry.IsAlive(matchRef);
        bool got = registry.TryGet(matchRef, out AssetLease<RawJsonPayload> lease);
        using (lease)
        {
            Assert.True(alive, "IsAlive false after LoadAsync.");
            Assert.True(got, "TryGet false after LoadAsync.");
            Assert.NotNull(lease.Payload);
            Assert.Contains("MatchDuration", lease.Payload!.Json);
        }
        _ = contentDir;
    }

    /// <summary>Verifies content store and registry loading against the bundle layout.</summary>
    [Fact]
    public async Task Store_and_registry_against_bundle_layout()
    {
        string root = RepoRoot();
        string bundleContent = Path.Combine(root, "Source", "SSSuperGame.Client", "bin", "Debug", "net10.0", "karpik-bundle", "Content");
        Assert.True(Directory.Exists(bundleContent), "Build the Client launcher/bundle first.");
        string manifestPath = Path.Combine(bundleContent, "manifest.json");

        var fs = new RemappedFileSystem(bundleContent, bundleContent);
        var store = new FileContentStore(fs);

        var diagnostics = new List<ContentDiagnostic>();
        ContentManifest manifest = ContentManifest.LoadFromFile(manifestPath, diagnostics);
        var matchRef = new AssetRef<RawJsonPayload>("a80e8179-0861-41ca-a557-de8cfbeac225", 1);
        ContentManifestEntry matchEntry = Assert.Single(manifest.Entries, entry => entry.AssetId.Equals(matchRef.Id));
        string locator = matchEntry.ArtifactLocator;
        ReadOnlyMemory<byte> mem = store.Get(locator);
        string storeText = System.Text.Encoding.UTF8.GetString(mem.Span);
        string cookedPath = Path.Combine(bundleContent, locator.Replace('/', Path.DirectorySeparatorChar));
        string cookedText = File.ReadAllText(cookedPath);
        Assert.Equal(cookedText, storeText);

        var registry = new ContentRegistry(fs, store);
        registry.RegisterManifest(manifest, store);
        await registry.LoadAsync(matchRef, CancellationToken.None);
        Assert.True(registry.IsAlive(matchRef));
        Assert.True(registry.TryGet(matchRef, out AssetLease<RawJsonPayload> lease));
        using (lease)
        {
            Assert.NotNull(lease.Payload);
            Assert.Equal(cookedText, lease.Payload!.Json);

            var match = System.Text.Json.JsonSerializer.Deserialize<SSSuperGame.Shared.CoinRush.MatchConfig>(lease.Payload.Json);
            Assert.NotNull(match);
            Assert.Equal(90f, match!.MatchDuration);
        }
    }

    /// <summary>Verifies that the launcher includes the cooked UI strings asset.</summary>
    [Fact]
    public void Launcher_output_contains_ui_cooked_artifact()
    {
        string contentRoot = Path.Combine(
            RepoRoot(), "Source", "SSSuperGame.Client.Launcher", "bin", "Debug", "net10.0", "Content");
        string manifestPath = Path.Combine(contentRoot, "manifest.json");
        Assert.True(File.Exists(manifestPath), "Build the Client launcher first.");

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
        System.Text.Json.JsonElement entry = Assert.Single(
            document.RootElement.GetProperty("entries").EnumerateArray(),
            item => item.GetProperty("logicalName").GetString() == "game/CoinRush/UiStrings");
        string locator = entry.GetProperty("artifactLocator").GetString()!;
        string artifactPath = Path.Combine(contentRoot, locator.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(artifactPath), artifactPath);
    }
}
