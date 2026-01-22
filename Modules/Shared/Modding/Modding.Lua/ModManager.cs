using System.Collections.Concurrent;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.Log;
using Karpik.Jobs;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

public class ModManager : IModManager
{
    private readonly ConcurrentDictionary<string, ModContainer> _loadedMods = new();
    [DI] private IAssetsManager _assetsManager;
    [DI] private IServiceContainer _serviceProvider;
    private string _subFolder;
    
    private IFileSystem FileSystem => _assetsManager.FileSystem;

    public void Init(ExecutionSide caller)
    {
        _subFolder = caller switch
        {
            ExecutionSide.Client => "Client",
            ExecutionSide.Server => "Server",
            _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, null)
        };
    }
    
    public async JobHandle LoadMods(string modsRootDirectory)
    {
        if (!FileSystem.ExistsDirectory(modsRootDirectory))
        {
            modsRootDirectory = FileSystem.Combine(_assetsManager.ModsPath, modsRootDirectory);
            if (!FileSystem.ExistsDirectory(modsRootDirectory))
            {
                await Logger.Instance.Log(nameof(ModManager), $"Mods directory not found: {modsRootDirectory}", LogLevel.Error);
                return;
            }
        }
        
        await foreach (var modDir in FileSystem.GetDirectories(modsRootDirectory).ToArray().ToAsyncEnumerable())
        {
            await LoadMod(modDir);
        }
        
        LoadMods();
    }
    
    private async JobHandle LoadMod(string modDirectory)
    {
        try
        {
            string metadataPath = FileSystem.Combine(modDirectory, "mod_info.json");
            if (!FileSystem.Exists(metadataPath))
            {
                await Logger.Instance.Log(nameof(ModManager), $"Mod missing mod.json: {modDirectory}", LogLevel.Error);
                return;
            }

            var handle = await _assetsManager.LoadAssetAsync<ModMetaDataAsset>(metadataPath);
            var metaData = handle.Asset.MetaData;
            if (string.IsNullOrEmpty(handle.Asset.MetaData.Id))
            {
                await Logger.Instance.Log(nameof(ModManager), $"Invalid mod metadata in {modDirectory}", LogLevel.Error);
                return;
            }
            
            var container = new ModContainer(FileSystem.Combine(modDirectory, _subFolder), handle);
            _serviceProvider.Inject(container);
            container.Initialize();
            _loadedMods[metaData.Id] = container;
            
            await Logger.Instance.Log(nameof(ModManager), $"Mod loaded: {metaData.Name} v{metaData.Version} by {metaData.Author}");
        }
        catch (Exception ex)
        {
            await Logger.Instance.Log(nameof(ModManager), $"Error loading mod {modDirectory}: {ex}", LogLevel.Error);
        }
    }
    
    public void UpdateMods()
    {
        foreach (var container in _loadedMods.Values)
        {
            container.Update();
        }
    }
    
    public void StartMods()
    {
        foreach (var container in _loadedMods.Values)
        {
            container.Start();
        }
    }
    
    public void LoadMods()
    {
        foreach (var container in _loadedMods.Values)
        {
            container.Load();
        }
    }
    
    public void UnloadMods()
    {
        foreach (var container in _loadedMods.Values)
        {
            container.Unload();
        }
    }

    public async JobHandle ReloadAllMods(string modsRootDirectory)
    {
        UnloadMods();
        _loadedMods.Clear();
        await LoadMods(modsRootDirectory);
    }
    
    public ModMetaData GetModMetadata(string modId)
    {
        return _loadedMods.TryGetValue(modId, out var container) 
            ? container.MetaDataHandle.Asset.MetaData 
            : default;
    }
    
    public void ExecuteForMod(string modId, Action<Script> action)
    {
        if (!_loadedMods.TryGetValue(modId, out var container)) return;
        
        try
        {
            action(container.Script);
        }
        catch (Exception ex)
        {
            Logger.Instance.Log(nameof(ModManager), $"Execution error while executing {container.MetaDataHandle.Asset.MetaData.Id} mod: {ex}", LogLevel.Error);
        }
    }
    
    public void ExecuteForAllMods(Action<Script> action)
    {
        foreach (var container in _loadedMods.Values)
        {
            try
            {
                action(container.Script);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(nameof(ModManager), $"Execution error while executing {container.MetaDataHandle.Asset.MetaData.Id} mod: {ex}", LogLevel.Error);
            }
        }
    }

    public void Destroy()
    {
        foreach (var item in _loadedMods)
        {
            var container = item.Value;
            container.Destroy();
        }
        _loadedMods.Clear();
    }
}