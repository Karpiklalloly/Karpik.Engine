using System.Text;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core.FileSystem;
using Xunit;

public sealed class FileContentStoreTests
{
    [Fact]
    public void Get_ContainedLocator_ReadsThroughFileSystem()
    {
        var fileSystem = new FakeFileSystem("C:/game/Content", ("artifacts/a.cooked", "data"));

        ReadOnlyMemory<byte> result = new FileContentStore(fileSystem).Get("artifacts/a.cooked");

        Assert.Equal("data", Encoding.UTF8.GetString(result.Span));
        Assert.Equal(Path.GetFullPath("C:/game/Content/artifacts/a.cooked"), fileSystem.LastExistsPath);
        Assert.Equal(1, fileSystem.OpenReadCalls);
    }

    [Fact]
    public void Get_Traversal_ThrowsBeforeOpeningFile()
    {
        var fileSystem = new FakeFileSystem("C:/game/Content");

        Assert.Throws<InvalidDataException>(() => new FileContentStore(fileSystem).Get("../secret"));

        Assert.Equal(0, fileSystem.OpenReadCalls);
    }

    [Fact]
    public void Load_Stream_ParsesCanonicalManifest()
    {
        var assetId = new AssetId(Guid.Parse("2e4a8f56-fd60-4c3d-9a45-d7ba3d366e1c"));
        var manifest = new ContentManifest(ContentManifest.CurrentSchemaVersion,
        [
            new ContentManifestEntry(assetId, "raw-json", "game/config", "settings", "source", "artifacts/a.cooked", 4, [])
        ]);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(manifest.ToCanonicalJson()));

        ContentManifest result = ContentManifest.Load(stream);

        ContentManifestEntry entry = Assert.Single(result.Entries);
        Assert.Equal(assetId, entry.AssetId);
        Assert.Equal("artifacts/a.cooked", entry.ArtifactLocator);
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public FakeFileSystem(string contentPath, params (string Locator, string Contents)[] files)
        {
            ContentPath = Path.GetFullPath(contentPath);
            foreach ((string locator, string contents) in files)
            {
                _files[Path.GetFullPath(Path.Combine(ContentPath, locator))] = Encoding.UTF8.GetBytes(contents);
            }
        }

        public int OpenReadCalls { get; private set; }
        public string? LastExistsPath { get; private set; }
        public string RootPath => Path.GetPathRoot(ContentPath)!;
        public string ContentPath { get; }
        public string ModsPath => Path.Combine(RootPath, "Mods");
        public char DirectorySeparatorChar => Path.DirectorySeparatorChar;

        public bool Exists(string path)
        {
            LastExistsPath = path;
            return _files.ContainsKey(path);
        }
        public Stream OpenRead(string path)
        {
            OpenReadCalls++;
            return new MemoryStream(_files[path], writable: false);
        }

        public string GetExtension(string path) => Path.GetExtension(path);
        public bool ExistsDirectory(string path) => false;
        public Stream OpenWrite(string path) => throw new NotSupportedException();
        public string Combine(params ReadOnlySpan<string> path) => Path.Combine(path);
        public Span<string> GetDirectories(string path) => [];
        public Span<string> GetFiles(string path) => [];
        public Span<string> GetFiles(string path, string searchPattern, SearchOption searchOption) => [];
        public string GetFileName(string path) => Path.GetFileName(path);
    }
}
