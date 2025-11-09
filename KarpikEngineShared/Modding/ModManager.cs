using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding;

public class ModManager
{
    public enum Type
    {
        Server,
        Client,
    } 
        
    private readonly Dictionary<string, ModContainer> _loadedMods = new();
    [Inject] private Loader _loader;
    [Inject] private EcsDefaultWorld _world;
    private string _subFolder;

    public void Init(Type caller)
    {
        _subFolder = caller switch
        {
            Type.Client => "Client",
            Type.Server => "Server",
            _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, null)
        };
    }
    
    public void LoadMods(string modsRootDirectory)
    {
        if (!Directory.Exists(modsRootDirectory))
        {
            modsRootDirectory = Path.Combine(_loader.RootPath, modsRootDirectory);
            if (!Directory.Exists(modsRootDirectory)) return;
        }
        
        foreach (var modDir in Directory.GetDirectories(modsRootDirectory))
        {
            LoadMod(modDir);
        }
        
        LoadMods();
    }
    
    private void LoadMod(string modDirectory)
    {
        try
        {
            // Загрузка метаданных
            string metadataPath = Path.Combine(modDirectory, "mod_info.json");
            var files = Directory.GetFiles(modDirectory);
            if (!File.Exists(metadataPath))
            {
                Logger.Instance.Log($"Mod missing mod.json: {modDirectory}");
                return;
            }
            
            var metadata = JsonConvert.DeserializeObject<ModMetaData>(File.ReadAllText(metadataPath));
            if (string.IsNullOrEmpty(metadata.Id))
            {
                Logger.Instance.Log($"Invalid mod metadata in {modDirectory}");
                return;
            }
            
            // Создаем контейнер мода
            var container = new ModContainer(Path.Combine(modDirectory, _subFolder), metadata, _world);
            container.Initialize();
            _loadedMods[metadata.Id] = container;
            
            Logger.Instance.Log($"Mod loaded: {metadata.Name} v{metadata.Version} by {metadata.Author}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Error loading mod {modDirectory}: {ex.Message}");
        }
    }
    
    public void UpdateMods()
    {
        foreach (var container in _loadedMods.Values)
        {
            container.Update();
        }
    }
    
    public void DebugUpdateMods()
    {
        foreach (var container in _loadedMods.Values)
        {
            container.DebugUpdate();
        }
    }

    public void FixedUpdateMods()
    {
        foreach (var container in _loadedMods.Values)
        {
            container.FixedUpdate();
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

    public void ReloadAllMods(string modsRootDirectory)
    {
        UnloadMods();
        _loadedMods.Clear();
        LoadMods(modsRootDirectory);
    }
    
    public ModMetaData GetModMetadata(string modId)
    {
        return _loadedMods.TryGetValue(modId, out var container) 
            ? container.MetaData 
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
            Logger.Instance.Log($"[{container.MetaData.Id}] Execution error: {ex.Message}");
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
                Logger.Instance.Log($"[{container.MetaData.Id}] Execution error: {ex.Message}");
            }
        }
    }
}