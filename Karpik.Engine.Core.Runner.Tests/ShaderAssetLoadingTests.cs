using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.AssetManagement.Core.Physical;
using Karpik.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Collection(nameof(CurrentDirectoryCollection))]
public sealed class ShaderAssetLoadingTests
{
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

            var manager = new AssetsManager(
                NullLogger<AssetsManager>.Instance,
                new PhysicalFileSystem(),
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
        }
        finally
        {
            Environment.CurrentDirectory = originalWorkingDirectory;
            jobs.WaitForCompletion();
            jobs.Shutdown();
            Directory.Delete(bundleRoot, recursive: true);
        }
    }
}
