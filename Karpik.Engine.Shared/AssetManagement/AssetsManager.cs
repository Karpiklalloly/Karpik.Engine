using System.Collections.Concurrent;

namespace Karpik.Engine.Shared;

public class AssetsManager
{
    public string RootPath => AppDomain.CurrentDomain.BaseDirectory;
    public string ContentPath => Path.Combine(RootPath, "Content");
    public string ModsPath => Path.Combine(RootPath, "Mods");
    
    // [Hash, Asset Type] -> [Asset Instance]
    private readonly ConcurrentDictionary<(int, Type), Asset> _loadedAssets = new();
    
    // [Extension, Asset Type] -> [Loader]
    private readonly ConcurrentDictionary<(string, Type), IAssetLoader> _loaders = new();
    
    // [Asset Type] -> [Saver]
    private readonly ConcurrentDictionary<Type, IAssetSaver> _savers = new();
    
    private readonly IFileSystem _fileSystem;
    private IServiceProvider _serviceProvider;

    public AssetsManager(IFileSystem fileSystem = null)
    {
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
    }
    
    public void RegisterSaver<T>(IAssetSaver saver) where T : Asset
    {
        _serviceProvider.Inject(saver);
        _savers[typeof(T)] = saver;
    }
    
    public void RegisterLoader<T>(IAssetLoader loader, params string[] extensions) where T : Asset
    {
        _serviceProvider.Inject(loader);
        foreach (var extension in extensions)
        {
            string safeExt = NormalizeExtension(extension);
            _loaders[(safeExt, typeof(T))] = loader;
        }
    }
    
    public async Task<AssetHandle<T>> LoadAssetAsync<T>(string path) where T : Asset
    {
        int id = AssetPath.GetHash(path);

        if (_loadedAssets.TryGetValue((id, typeof(T)), out var existingAsset))
        {
            return new AssetHandle<T>((T)existingAsset, this);
        }

        var extension = NormalizeExtension(Path.GetExtension(path));

        if (!_loaders.TryGetValue((extension, typeof(T)), out var loader))
        {
            throw new NotSupportedException($"No loader registered for extension '{extension}' and type '{typeof(T).Name}'");
        }

        if (!_fileSystem.Exists(path)) throw new FileNotFoundException(path);

        await using Stream stream = _fileSystem.OpenRead(path);

        var newAsset = await loader.LoadAsync(stream, path);
        newAsset.Id = id;
        newAsset.Path = path;
        newAsset.SourceType = typeof(T);

        _loadedAssets.TryAdd((id, typeof(T)), newAsset);
        return new AssetHandle<T>((T)newAsset, this);
    }
    
    public async Task SaveAssetAsync(Asset asset, string path = null)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));

        string targetPath = path ?? asset.Path;
        if (string.IsNullOrEmpty(targetPath))
        {
            throw new InvalidOperationException("Cannot save asset: Path is missing.");
        }

        Type type = asset.GetType();
        if (!_savers.TryGetValue(type, out IAssetSaver saver))
        {
            throw new NotSupportedException($"No saver registered for type '{type.Name}'");
        }

        await using (Stream stream = _fileSystem.OpenWrite(targetPath))
        {
            await saver.SaveAsync(asset, stream);
        }
        
        if (asset.Path != targetPath)
        {
            int oldId = asset.Id;
            int newId = AssetPath.GetHash(targetPath);
            Type assetType = asset.SourceType;

            var oldKey = (oldId, assetType);
            var newKey = (newId, assetType);

            if (_loadedAssets.ContainsKey(newKey))
            {
                throw new InvalidOperationException($"Cannot rename asset to '{targetPath}' because another asset is already loaded with this path.");
            }

            if (_loadedAssets.Remove(oldKey, out _))
            {
                _loadedAssets.TryAdd(newKey, asset);
            
                Console.WriteLine($"[AssetManager] Cache updated: moved from {oldId} to {newId}");
            }
            else
            {
                _loadedAssets.TryAdd(newKey, asset);
                asset.IncrementRef();
            }

            asset.Path = targetPath;
            asset.Id = newId;
        }
    }
    
    internal void ReleaseAsset(Asset asset)
    {
        if (asset == null) return;

        if (asset.DecrementRef())
        {
            _loadedAssets.Remove((asset.Id, asset.SourceType), out _);
            asset.Unload();
        }
    }
    
    private string NormalizeExtension(string ext)
    {
        return ext.StartsWith(".", StringComparison.InvariantCultureIgnoreCase)
            ? ext.ToLowerInvariant()
            : "." + ext.ToLowerInvariant();
    }
}