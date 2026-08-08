using System.Collections.Concurrent;
using System.Composition;
using System.Reflection;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.AssetManagement.Core;

[Export(typeof(IAssetsManager))]
[Export(typeof(AssetsManager))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
internal class AssetsManager : IAssetsManager
{
    public string RootPath => FileSystem.RootPath;
    public string ContentPath => FileSystem.ContentPath;
    public string ModsPath => FileSystem.ModsPath;
    public IFileSystem FileSystem => _fileSystem;
    
    // [Hash, Asset Type] -> [Asset Instance]
    private readonly ConcurrentDictionary<(int, Type), Asset> _loadedAssets = new();
    
    // [Extension, Asset Type] -> [Loader]
    private readonly ConcurrentDictionary<(string, Type), IAssetLoader> _loaders = new();
    
    // [Asset Type] -> [Saver]
    private readonly ConcurrentDictionary<Type, IAssetSaver> _savers = new();
    
    private readonly IFileSystem _fileSystem;
    private readonly AssetLoadContext _assetLoadContext;

    public AssetsManager(IFileSystem fileSystem, IAssetSaver[] savers, IAssetLoader[] loaders)
    {
        _fileSystem = fileSystem;
        _assetLoadContext = new AssetLoadContext(this);
        RegisterSavers(savers);
        RegisterLoaders(loaders);
    }
    
    public void RegisterSaver(IAssetSaver saver)
    {
        RegisterSaverInternal(saver);
    }
    
    public void RegisterLoader(IAssetLoader loader)
    {
        RegisterLoaderInternal(loader);
    }

    private void RegisterSavers(IAssetSaver[] savers)
    {
        foreach (var saver in savers)
        {
            RegisterSaver(saver);
        }
    }
    
    private void RegisterLoaders(IAssetLoader[] loaders)
    {
        foreach (var loader in loaders)
        {
            RegisterLoader(loader);
        }
    }

    private void RegisterLoaderInternal(IAssetLoader loader)
    {
        foreach (var extension in loader.SupportedExtensions)
        {
            // TODO: Подумать, что делать с перезаписью (мб приоритет в интерфейсе сделать)
            string safeExt = NormalizeExtension(extension);
            _loaders[(safeExt, loader.AssetType)] = loader;
        }
    }
    
    private void RegisterSaverInternal(IAssetSaver saver)
    {
        _savers[saver.AssetType] = saver;
    }
    
    public async JobHandle<AssetHandle<T>> LoadAssetAsync<T>(string path) where T : Asset
    {
        var asset = await LoadAssetInternal(path, typeof(T));
        return new AssetHandle<T>((T)asset, this);
    }

    public async JobHandle<AssetHandle<Asset>> LoadAssetByPathAsync(string path)
    {
        var ext = NormalizeExtension(Path.GetExtension(path));

        foreach (var pair in _loaders.Where(x => x.Key.Item1 == ext))
        {
            var loaderEntry = pair;

            if (loaderEntry.Value == null) continue;
            var concreteType = loaderEntry.Key.Item2;
            var asset = await LoadAssetInternal(path, concreteType);

            return new AssetHandle<Asset>(asset, this);
        }
        
        throw new NotSupportedException($"No loader registered for extension '{ext}'");
    }
    
    private async JobHandle<Asset> LoadAssetInternal(string path, Type assetType)
    {
        int id = AssetPath.GetHash(path);
        var cacheKey = (id, assetType);
        
        if (_loadedAssets.TryGetValue(cacheKey, out var existingAsset))
        {
            return existingAsset;
        }

        var extension = NormalizeExtension(_fileSystem.GetExtension(path));
        var loaderKey = (extension, assetType);

        if (!_loaders.TryGetValue(loaderKey, out var loader))
        {
            throw new NotSupportedException($"No loader registered for extension '{extension}' and type '{assetType.Name}'");
        }
        
        string targetPath = path;
        
        if (!_fileSystem.Exists(targetPath))
        {
            var modsSubPath = _fileSystem.Combine(ModsPath, targetPath);
            targetPath = _fileSystem.Exists(modsSubPath)
                ? modsSubPath
                : _fileSystem.Combine(ContentPath, targetPath);
        }

        if (!_fileSystem.Exists(targetPath))
        {
            if (loader.DefaultPath is null) throw new FileNotFoundException($"Asset not found: {path}");
            await Logger.Instance.Log(nameof(AssetsManager), $"Not found {targetPath}, loading default asset {loader.DefaultPath}.", LogLevel.Warning);
            targetPath = loader.DefaultPath;
        }
        
        if (!_fileSystem.Exists(targetPath))
        {
            var modsSubPath = _fileSystem.Combine(ModsPath, targetPath);
            targetPath = _fileSystem.Exists(modsSubPath)
                ? modsSubPath
                : _fileSystem.Combine(ContentPath, targetPath);
        }

        await using Stream stream = _fileSystem.OpenRead(targetPath);

        var newAsset = await loader.LoadAsync(_assetLoadContext, stream, targetPath);
        newAsset.Id = id;
        newAsset.Path = targetPath;
        newAsset.Type = assetType;
        newAsset.Manager = this;

        _loadedAssets.TryAdd(cacheKey, newAsset);
        newAsset.Load();
        return newAsset;
    }
    
    public async JobHandle<AssetHandle<T>> SaveAssetAsync<T>(T asset, string? path = null)  where T : Asset
    {
        if (asset == null) throw new NullReferenceException();

        string targetPath = path ?? asset.Path;
        if (string.IsNullOrEmpty(targetPath))
        {
            throw new InvalidOperationException("Cannot save asset: Path is missing.");
        }
        
        if (!_fileSystem.Exists(targetPath))
        {
            var modsSubPath = _fileSystem.Combine(ModsPath, targetPath);
            targetPath = _fileSystem.Exists(modsSubPath)
                ? modsSubPath
                : _fileSystem.Combine(ContentPath, targetPath);
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
            Type assetType = asset.Type;
            
            var oldKey = (oldId, assetType);
            var newKey = (newId, assetType);

            if (oldKey != newKey)
            {
                if (_loadedAssets.ContainsKey(newKey))
                {
                    throw new InvalidOperationException($"Cannot rename asset to '{targetPath}' because another asset is already loaded with this path.");
                }
            }

            if (_loadedAssets.Remove(oldKey, out _))
            {
                _loadedAssets.TryAdd(newKey, asset);

                await Logger.Instance.Log(nameof(AssetsManager), $"Cache updated: moved from {oldId} to {newId}");
            }
            else
            {
                _loadedAssets.TryAdd(newKey, asset);
            }

            asset.Path = targetPath;
            asset.Id = newId;
            asset.Type = assetType;
        }

        await Logger.Instance.Log($"{asset} saved to {targetPath}");
        return new AssetHandle<T>(asset, this);
    }

    void IAssetsManager.ReleaseAsset(Asset asset)
    {
        ReleaseAsset(asset);
    }

    internal void ReleaseAsset(Asset asset)
    {
        if (asset.DecrementRef())
        {
            _loadedAssets.Remove((asset.Id, asset.Type), out _);
            asset.Unload();
        }
    }

    internal void ReleaseAll()
    {
        var assets = _loadedAssets.Values;
        foreach (var asset in assets)
        {
            while (asset.RefCount > 0)
            {
                ReleaseAsset(asset);
            }
        }
    }
    
    private string NormalizeExtension(string ext)
    {
        return ext.StartsWith('.')
            ? ext.ToLowerInvariant()
            : "." + ext.ToLowerInvariant();
    }

    public void Dispose()
    {
        ReleaseAll();
    }
}
