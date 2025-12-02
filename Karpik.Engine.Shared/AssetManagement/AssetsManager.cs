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
    [DI] private IServiceProvider _serviceProvider;

    public AssetsManager(IFileSystem fileSystem = null)
    {
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
    }
    
    public void RegisterSaver<T>(IAssetSaver saver) where T : Asset
    {
        RegisterSaverInternal(saver, typeof(T));
    }
    
    public void RegisterLoader<T>(IAssetLoader loader) where T : Asset
    {
        RegisterLoaderInternal(loader, typeof(T));
    }

    private void RegisterLoaderInternal(IAssetLoader loader, Type assetType)
    {
        _serviceProvider.Inject(loader);
        foreach (var extension in loader.SupportedExtensions)
        {
            string safeExt = NormalizeExtension(extension);
            _loaders[(safeExt, assetType)] = loader;
        }
    }
    
    private void RegisterSaverInternal(IAssetSaver saver, Type assetType)
    {
        _serviceProvider.Inject(saver);
        _savers[assetType] = saver;
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
        
        string targetPath = path;
        
        if (!_fileSystem.Exists(targetPath))
        {
            var modsSubPath = _fileSystem.Combine(ModsPath, targetPath);
            targetPath = _fileSystem.Exists(modsSubPath)
                ? modsSubPath
                : _fileSystem.Combine(ContentPath, targetPath);
        }

        if (!_fileSystem.Exists(targetPath)) throw new FileNotFoundException(targetPath);

        await using Stream stream = _fileSystem.OpenRead(targetPath);

        var newAsset = await loader.LoadAsync(stream, targetPath);
        newAsset.Id = id;
        newAsset.Path = targetPath;
        newAsset.Type = typeof(T);

        _loadedAssets.TryAdd((id, typeof(T)), newAsset);
        return new AssetHandle<T>((T)newAsset, this);
    }
    
    public async Task<AssetHandle<T>> SaveAssetAsync<T>(T asset, string path = null)  where T : Asset
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
            
                Console.WriteLine($"[AssetManager] Cache updated: moved from {oldId} to {newId}");
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
        return new(asset, this);
    }
    
    internal void ReleaseAsset(Asset asset)
    {
        if (asset == null) return;

        if (asset.DecrementRef())
        {
            _loadedAssets.Remove((asset.Id, SourceType: asset.Type), out _);
            asset.Unload();
        }
    }
    
    private string NormalizeExtension(string ext)
    {
        return ext.StartsWith(".", StringComparison.InvariantCultureIgnoreCase)
            ? ext.ToLowerInvariant()
            : "." + ext.ToLowerInvariant();
    }

    public void FindAllLoaders()
    {
        var loaderTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IAssetLoader).IsAssignableFrom(type)
                           && !type.IsInterface
                           && !type.IsAbstract
                           && !type.IsGenericType);

        foreach (var loaderType in loaderTypes)
        {
            try
            {
                var loaderInstance = (IAssetLoader)Activator.CreateInstance(loaderType)!;
                Type assetType = loaderInstance.AssetType;
                RegisterLoaderInternal(loaderInstance, assetType);
            }
            catch (Exception e)
            {
                Logger.Instance.Log($"[AssetManager] Failed to auto-register loader {loaderType.Name}: {e.Message}", LogLevel.Error);
            }
        }
    }
    
    public void FindAllSavers()
    {
        var loaderTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IAssetSaver).IsAssignableFrom(type)
                           && !type.IsInterface
                           && !type.IsAbstract
                           && !type.IsGenericType);

        foreach (var loaderType in loaderTypes)
        {
            try
            {
                var loaderInstance = (IAssetSaver)Activator.CreateInstance(loaderType)!;
                RegisterSaverInternal(loaderInstance, loaderInstance.AssetType);
            }
            catch (Exception e)
            {
                Logger.Instance.Log($"[AssetManager] Failed to auto-register loader {loaderType.Name}: {e.Message}", LogLevel.Error);
            }
        }
    }
}