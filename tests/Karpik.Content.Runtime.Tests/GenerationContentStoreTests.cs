using System.Text.Json;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core.FileSystem;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class GenerationContentStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOnlyPublishedPackageKeepsOrdinaryStartup_ButNeverHidesCorruptMetadata(bool corruptMetadata)
    {
        string workspace = Path.Combine(Path.GetTempPath(), "karpik-readonly-content-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(workspace, "source"), output = Path.Combine(workspace, "output");
        Directory.CreateDirectory(source);
        string gate = Path.Combine(output, ".karpik-generations.gate");
        FileAttributes attributes = default;
        try
        {
            AssetId id = new(Guid.NewGuid());
            WriteSource(source, "a", id, "{\"v\":1}");
            using var build = new ContentBuildCoordinator().Build(new ContentBuildOptions
            { SourceRoot = source, OutputRoot = output, Namespace = "game", Target = AssetTarget.Client });
            Assert.True(build.Success);
            if (corruptMetadata)
                File.WriteAllText(Path.Combine(output, ".generations", build.GenerationId!, "generation.json"), "{}");
            attributes = File.GetAttributes(gate);
            File.SetAttributes(gate, attributes | FileAttributes.ReadOnly);
            if (corruptMetadata)
            {
                Assert.Throws<InvalidDataException>(() => new FileContentStore(new PhysicalFileSystem(output)));
                return;
            }
            using var store = new FileContentStore(new PhysicalFileSystem(output));
            Assert.False(store.IsGenerationCaptured); // Development opt-in must reject a non-owning fallback.
            await using var registry = new ContentRegistry(new PhysicalFileSystem(output), store);
            registry.Start();
            Assert.True(registry.TryResolve<RawJsonPayload>(id, out var reference));
            await registry.LoadAsync(reference);
            Assert.Equal("{\"v\":1}", ReadJson(registry, reference));
        }
        finally
        {
            if (File.Exists(gate)) File.SetAttributes(gate, attributes);
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedStartupAndUnloadedUnchangedSlotsRetainTheirOriginalBytes(bool migrateLegacyStartup)
    {
        string workspace = Path.Combine(Path.GetTempPath(), "karpik-captured-store-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(workspace, "source");
        string output = Path.Combine(workspace, "output");
        Directory.CreateDirectory(source);
        try
        {
            AssetId a = new(Guid.NewGuid()), b = new(Guid.NewGuid());
            WriteSource(source, "a", a, "{\"v\":1}");
            WriteSource(source, "b", b, "{\"v\":1}");
            var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = "game", Target = AssetTarget.Client };
            using var initial = new ContentBuildCoordinator().Build(options);
            Assert.True(initial.Success);
            if (migrateLegacyStartup)
            {
                Directory.Delete(Path.Combine(output, ".generations"), recursive: true);
                File.Delete(Path.Combine(output, ".karpik-current-generation"));
            }
            using var startup = new FileContentStore(new PhysicalFileSystem(output));
            Assert.True(startup.IsGenerationCaptured);
            if (!migrateLegacyStartup) Assert.Equal(initial.GenerationId, startup.GenerationId);
            Assert.Equal(!migrateLegacyStartup, startup.IdentityKnown);
            string firstRoot = Path.Combine(output, ".generations", startup.GenerationId!);
            await using var registry = new ContentRegistry(new PhysicalFileSystem(output), startup);
            registry.Start();
            WriteSource(source, "a", a, "{\"v\":2}");
            using var second = new ContentBuildCoordinator().Build(options);
            using (var next = GenerationContentStore.Open(output, second.GenerationId!))
            {
                using var prepared = await registry.PrepareAsync(next.Manifest, next);
                next.Dispose(); // Ownership has transferred to prepared slots/snapshot.
                Assert.True(registry.TryCommit(prepared));
                await prepared.Completion;
            }
            Assert.True(Directory.Exists(firstRoot));
            Assert.True(registry.TryResolve<RawJsonPayload>(b, out var unchanged));
            Assert.Equal(1u, unchanged.Version);
            await registry.LoadAsync(unchanged);
            Assert.Equal("{\"v\":1}", ReadJson(registry, unchanged));
            Assert.True(registry.TryResolve<RawJsonPayload>(a, out var changed));
            await registry.LoadAsync(changed);
            Assert.Equal("{\"v\":2}", ReadJson(registry, changed));
            File.Delete(Path.Combine(source, "b.json"));
            File.Delete(Path.Combine(source, "b.json.meta"));
            using var third = new ContentBuildCoordinator().Build(options);
            using (var next = GenerationContentStore.Open(output, third.GenerationId!))
            {
                using var prepared = await registry.PrepareAsync(next.Manifest, next);
                next.Dispose();
                Assert.True(registry.TryCommit(prepared));
                await prepared.Completion;
            }
            Assert.False(Directory.Exists(firstRoot));
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }

    private static string ReadJson(ContentRegistry registry, AssetRef<RawJsonPayload> reference)
    {
        Assert.True(registry.TryGet(reference, out var lease));
        return lease.Payload.Json;
    }

    private static void WriteSource(string root, string name, AssetId id, string json)
    {
        File.WriteAllText(Path.Combine(root, name + ".json"), json);
        File.WriteAllText(Path.Combine(root, name + ".json.meta"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, assetId = id.ToCanonicalString(), declaredType = "raw-json", logicalName = "game/" + name,
            importSettings = new { }, dependencies = Array.Empty<string>()
        }));
    }

    private sealed class PhysicalFileSystem(string contentPath) : IFileSystem
    {
        public string RootPath => Path.GetPathRoot(ContentPath)!;
        public string ContentPath => contentPath;
        public string ModsPath => Path.Combine(contentPath, "Mods");
        public char DirectorySeparatorChar => Path.DirectorySeparatorChar;
        public bool Exists(string path) => File.Exists(path);
        public bool ExistsDirectory(string path) => Directory.Exists(path);
        public Stream OpenRead(string path) => File.OpenRead(path);
        public Stream OpenWrite(string path) => File.OpenWrite(path);
        public string GetExtension(string path) => Path.GetExtension(path);
        public string Combine(params ReadOnlySpan<string> path) => Path.Combine(path);
        public Span<string> GetDirectories(string path) => Directory.GetDirectories(path);
        public Span<string> GetFiles(string path) => Directory.GetFiles(path);
        public Span<string> GetFiles(string path, string searchPattern, SearchOption searchOption) => Directory.GetFiles(path, searchPattern, searchOption);
        public string GetFileName(string path) => Path.GetFileName(path);
    }
}
