using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Collection(nameof(CurrentDirectoryCollection))]
public sealed class ShaderAssetLoadingTests
{
    [Theory]
    [InlineData("Shaders/2D.vert", "Shaders/2D.vert")]
    [InlineData("game/Shaders/2D.vert", "game/Shaders/2D.vert")]
    [InlineData("Shaders\\2D.vert", "Shaders\\2D.vert")]
    [InlineData("Shaders/missing.vert", "Shaders/2D.vert")]
    public void LoadAssetAsync_ResolvesShaderFromManifestWithoutLooseSource(string path, string loadedPath)
    {
        string originalWorkingDirectory = Environment.CurrentDirectory;
        string bundleRoot = Path.Combine(Path.GetTempPath(), "karpik-cooked-shader-" + Guid.NewGuid().ToString("N"));
        string contentRoot = Path.Combine(bundleRoot, "Content");
        Directory.CreateDirectory(Path.Combine(contentRoot, "artifacts"));
        const string shader = "#version 450\nvoid main() {}\n";
        File.WriteAllText(Path.Combine(contentRoot, "artifacts", "shader.cooked"), shader);
        var manifest = new ContentManifest(ContentManifest.CurrentSchemaVersion,
        [
            new ContentManifestEntry(new AssetId(Guid.NewGuid()), "shader", "game/Shaders/2D.vert",
                "settings", "source", "artifacts/shader.cooked", shader.Length, [])
        ]);
        string manifestPath = Path.Combine(contentRoot, "manifest.json");
        File.WriteAllText(manifestPath, manifest.ToCanonicalJson());
        var jobs = new JobSystem(workerCount: 2, prefix: nameof(ShaderAssetLoadingTests));
        try
        {
            Environment.CurrentDirectory = bundleRoot;
            Job.Initialize(jobs);
            var fileSystem = new PhysicalFileSystem();
            var store = new FileContentStore(fileSystem);
            var registry = new ContentRegistry(fileSystem, store);
            var manager = new AssetsManager(NullLogger<AssetsManager>.Instance, fileSystem, registry, store, [], [new FallbackShaderLoader()]);

            using (AssetHandle<ShaderAsset> handle = manager.LoadAssetAsync<ShaderAsset>(path).GetAwaiter().GetResult())
            {
                Assert.Equal(shader, System.Text.Encoding.UTF8.GetString(handle.Asset!.ShaderBytes));
                Assert.Equal(loadedPath, handle.Asset.Path);
            }
            Assert.False(File.Exists(Path.Combine(contentRoot, "Shaders", "2D.vert")));

            // A later Autofac Start must not reset the manifest used during graphics startup.
            registry.Start();
            Assert.True(registry.TryResolveArtifact("game/Shaders/2D.vert", out string locator, out _));
            Assert.Equal("artifacts/shader.cooked", locator);

            File.Delete(Path.Combine(contentRoot, "artifacts", "shader.cooked"));
            Assert.Throws<InvalidDataException>(() =>
                manager.LoadAssetAsync<ShaderAsset>("game/Shaders/2D.vert").GetAwaiter().GetResult());

            var replacementStore = new ReplacementShaderStore();
            registry.RegisterManifest(manifest, replacementStore);
            using AssetHandle<ShaderAsset> replacement = manager.LoadAssetAsync<ShaderAsset>(path).GetAwaiter().GetResult();
            Assert.Equal("replacement shader", System.Text.Encoding.UTF8.GetString(replacement.Asset!.ShaderBytes));
        }
        finally
        {
            Environment.CurrentDirectory = originalWorkingDirectory;
            Job.ShutdownIfCurrent(jobs);
            Directory.Delete(bundleRoot, recursive: true);
        }
    }

    private sealed class FallbackShaderLoader : ShaderLoader
    {
        public override string? DefaultPath => "Shaders/2D.vert";
    }

    private sealed class ReplacementShaderStore : IContentStore
    {
        public Stream OpenRead(string locator) => new MemoryStream(Get(locator).ToArray(), writable: false);
        public ReadOnlyMemory<byte> Get(string locator) => System.Text.Encoding.UTF8.GetBytes("replacement shader");
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Task.FromResult(Get(locator));
    }

    [Fact]
    public void LoadAssetAsync_CompletesForShaderLoadedByWorker()
    {
        string originalWorkingDirectory = Environment.CurrentDirectory;
        string bundleRoot = Path.Combine(
            Path.GetTempPath(),
            "karpik-shader-load-" + Guid.NewGuid().ToString("N"));
        string shaderDirectory = Path.Combine(bundleRoot, "Content", "Shaders");
        Directory.CreateDirectory(shaderDirectory);
        File.WriteAllText(Path.Combine(shaderDirectory, "2D.vert"), "#version 450\nvoid main() {}\n");
        File.WriteAllText(Path.Combine(shaderDirectory, "2D.frag"), "#version 450\nvoid main() {}\n");
        File.WriteAllText(Path.Combine(shaderDirectory, "TextSdf.frag"), "#version 450\nvoid main() {}\n");

        var jobs = new JobSystem(workerCount: 2, prefix: nameof(ShaderAssetLoadingTests));
        try
        {
            Environment.CurrentDirectory = bundleRoot;
            Job.Initialize(jobs);

            var fileSystem = new PhysicalFileSystem();
            var store = new FileContentStore(fileSystem);
            var registry = new ContentRegistry(fileSystem, store);
            var manager = new AssetsManager(
                NullLogger<AssetsManager>.Instance,
                fileSystem,
                registry,
                store,
                [],
                [new ShaderLoader()]);

            JobHandle<AssetHandle<ShaderAsset>> fragmentPreload =
                manager.LoadAssetAsync<ShaderAsset>("Shaders/2D.frag");
            JobHandle<AssetHandle<ShaderAsset>> textFragmentPreload =
                manager.LoadAssetAsync<ShaderAsset>("Shaders/TextSdf.frag");
            JobHandle<AssetHandle<ShaderAsset>> vertexPreload =
                manager.LoadAssetAsync<ShaderAsset>("Shaders/2D.vert");
            JobHandle<JobHandle> preloadCoordinator = Job.Run<JobHandle>(async () =>
            {
                using AssetHandle<ShaderAsset> fragment = await fragmentPreload;
                using AssetHandle<ShaderAsset> textFragment = await textFragmentPreload;
                using AssetHandle<ShaderAsset> vertex = await vertexPreload;
            });

            JobHandle<AssetHandle<ShaderAsset>> load = manager.LoadAssetAsync<ShaderAsset>("Shaders/2D.vert");

            Assert.True(
                SpinWait.SpinUntil(() => load.IsCompleted, TimeSpan.FromSeconds(5)),
                "Shader asset load did not complete.");

            using AssetHandle<ShaderAsset> handle = load.GetAwaiter().GetResult();
            Assert.NotEmpty(handle.Asset.ShaderBytes);
            Assert.True(
                SpinWait.SpinUntil(() => preloadCoordinator.IsCompleted, TimeSpan.FromSeconds(5)),
                "Concurrent shader preload did not complete.");

            var fallbackManager = new AssetsManager(NullLogger<AssetsManager>.Instance, fileSystem, registry, store, [], [new FallbackShaderLoader()]);
            using AssetHandle<ShaderAsset> fallback = fallbackManager.LoadAssetAsync<ShaderAsset>("Shaders/missing.vert").GetAwaiter().GetResult();
            Assert.Equal("Shaders/2D.vert", fallback.Asset!.Path);
        }
        finally
        {
            Environment.CurrentDirectory = originalWorkingDirectory;
            Job.ShutdownIfCurrent(jobs);
            Directory.Delete(bundleRoot, recursive: true);
        }
    }
}
