using System.Collections.Concurrent;
using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.Log;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IModManager))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class ModManager(
    ILogger<ModManager> logger,
    ILogger<ModContainer> containerLogger,
    ILogger<GameAPI> gameApiLogger,
    IAssetsManager assetsManager, Time time) : IModManager
{
    private readonly ConcurrentDictionary<string, ModContainer> _loadedMods = new();
    private string _subFolder;
    
    private IFileSystem FileSystem => assetsManager.FileSystem;

    public void Init(ExecutionSide caller)
    {
        UserData.RegisterType<GameAPI>();
        
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
            modsRootDirectory = FileSystem.Combine(assetsManager.ModsPath, modsRootDirectory);
            if (!FileSystem.ExistsDirectory(modsRootDirectory))
            {
                logger.LogError("Mods directory not found: {ModsRootDirectory}", modsRootDirectory);
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
                logger.LogError("Mod missing mod.json: {ModDirectory}", modDirectory);
                return;
            }

            var handle = await assetsManager.LoadAssetAsync<ModMetaDataAsset>(metadataPath);
            if (!handle.IsValid
                || handle.Asset is null
                || string.IsNullOrEmpty(handle.Asset.MetaData.Id))
            {
                handle.Dispose();
                logger.LogError("Invalid mod metadata in {ModDirectory}", modDirectory);
                return;
            }
            
            var metaData = handle.Asset.MetaData;
            var container = new ModContainer(containerLogger, assetsManager, time, FileSystem.Combine(modDirectory, _subFolder), handle);
            
            try
            {
                await container.Initialize(gameApiLogger);
            }
            catch
            {
                container.Dispose();
                throw;
            }
            if (!_loadedMods.TryAdd(metaData.Id, container))
            {
                container.Dispose();

                logger.LogError("Duplicate mod id: {Id}", metaData.Id);
                return;
            }
            
            logger.LogDebug("Mod loaded: {Name} v{Version} by {Author}", metaData.Name, metaData.Version, metaData.Author);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading mod {ModDirectory}", modDirectory);
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
            container.Dispose();
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
            logger.LogError(ex, "Execution error while executing {Id} mod", container.MetaDataHandle.Asset.MetaData.Id);
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
                logger.LogError(ex, "Execution error while executing {Id} mod", container.MetaDataHandle.Asset.MetaData.Id);
            }
        }
    }

    public void Dispose()
    {
        foreach (var container in _loadedMods.Values)
        {
            container.Dispose();
        }

        _loadedMods.Clear();
        // TODO: на все симуляции сразу действует. Надо скоуп сделать
        UserData.UnregisterType<GameAPI>();
    }
}