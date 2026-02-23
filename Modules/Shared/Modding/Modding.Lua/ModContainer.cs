using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.Log;
using Karpik.Jobs;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

public class ModContainer : IModContainer
{
    public ModMetaData MetaData => MetaDataHandle.Asset.MetaData;
    public IFileSystem FileSystem => _assetsManager.FileSystem;
    public string DirectoryPath { get; }
    public AssetHandle<ModMetaDataAsset> MetaDataHandle { get; }
    public Script Script { get; }
    public bool IsEnabled { get; set; } = true;
    
    public IReadOnlyList<DynValue> UpdateFunctions => _updateFunction;
    public IReadOnlyList<DynValue> StartFunctions => _startFunction;
    public IReadOnlyList<DynValue> LoadFunctions => _loadFunction;
    public IReadOnlyList<DynValue> UnloadFunctions => _unloadFunction;
    
    // [DI] private EcsDefaultWorld _world;
    [DI] private IAssetsManager _assetsManager;
    [DI] private Time _time = null!;
    private readonly List<DynValue> _updateFunction = new();
    private readonly List<DynValue> _startFunction = new();
    private readonly List<DynValue> _loadFunction = new();
    private readonly List<DynValue> _unloadFunction = new();
    private readonly Dictionary<string, DynValue> _loadedModules = new();
    
    internal ModContainer(string directoryPath, AssetHandle<ModMetaDataAsset> metaDataHandle)
    {
        DirectoryPath = directoryPath;
        MetaDataHandle = metaDataHandle;
        Script = new Script();
        Script.Options.ScriptLoader = new ModScriptLoader(directoryPath, this);
        Script.Options.DebugPrint = s => Log(s);
    }

    internal void Initialize()
    {
        // Script.Globals["G"] = new GameAPI(MetaData.Id, this, _world);
        Script.Globals["G"] = new GameAPI(MetaData.Id, this);
        LoadRootScripts();
    }

    public DynValue LoadModule(string moduleName)
    {
        if (_loadedModules.TryGetValue(moduleName, out var module))
            return module;
        
        try
        {
            string path = moduleName.Replace('.', _assetsManager.FileSystem.DirectorySeparatorChar);
            if (!path.EndsWith(".lua")) path += ".lua";
            
            DynValue result = Script.DoFile(path);
            _loadedModules[moduleName] = result;
            return result;
        }
        catch (Exception ex)
        {
            Log($"Error loading module {moduleName}: {ex}", LogLevel.Error).GetAwaiter().GetResult();
            return DynValue.Nil;
        }
    }

    public void Update()
    {
        if (!IsEnabled) return;
        
        foreach (var func in _updateFunction)
        {
            try
            {
                Script.Call(func, _time.DeltaTime);
            }
            catch (Exception e)
            {
                Log($"Update error: {e}", LogLevel.Error).GetAwaiter().GetResult();
            }
        }
    }

    public void Start()
    {
        if (!IsEnabled) return;

        foreach (var func in _startFunction)
        {
            try
            {
                Script.Call(func);
            }
            catch (Exception e)
            {
                Log($"Start error: {e}", LogLevel.Error).GetAwaiter().GetResult();
            }
        }
    }
    
    public void Load()
    {
        if (!IsEnabled) return;

        foreach (var func in _loadFunction)
        {
            try
            {
                Script.Call(func);
            }
            catch (Exception e)
            {
                Log($"Load error: {e}", LogLevel.Error).GetAwaiter().GetResult();
            }
        }
    }
    
    public void Unload()
    {
        if (!IsEnabled) return;

        foreach (var func in _unloadFunction)
        {
            try
            {
                Script.Call(func);
            }
            catch (Exception e)
            {
                Log($"Unload error: {e}", LogLevel.Error).GetAwaiter().GetResult();
            }
        }
    }

    private async JobHandle LoadRootScripts()
    {
        try
        {
            var rootScripts = FileSystem.GetFiles(DirectoryPath, "*.lua", SearchOption.TopDirectoryOnly);

            await foreach (var scriptFile in rootScripts.ToArray().ToAsyncEnumerable())
            {
                try
                {
                    Script.DoStream(FileSystem.OpenRead(scriptFile));
                    
                    var updateFunction = Script.Globals.Get(EventModMethods.OnUpdate);
                    if (updateFunction.IsNotNil() && updateFunction.Type == DataType.Function)
                    {
                        _updateFunction.Add(updateFunction);
                        await Log($"Registered update for {_assetsManager.FileSystem.GetFileName(scriptFile)}");
                    }
                    
                    var startFunction = Script.Globals.Get(EventModMethods.OnStart);
                    if (startFunction.IsNotNil() && startFunction.Type == DataType.Function)
                    {
                        _startFunction.Add(startFunction);
                        await Log($"Registered start for {_assetsManager.FileSystem.GetFileName(scriptFile)}");
                    }
                    
                    var loadFunction = Script.Globals.Get(EventModMethods.OnLoad);
                    if (loadFunction.IsNotNil() && loadFunction.Type == DataType.Function)
                    {
                        _loadFunction.Add(loadFunction);
                        await Log($"Registered load for {_assetsManager.FileSystem.GetFileName(scriptFile)}");
                    }
                    
                    var unloadFunction = Script.Globals.Get(EventModMethods.OnUnload);
                    if (unloadFunction.IsNotNil() && unloadFunction.Type == DataType.Function)
                    {
                        _unloadFunction.Add(unloadFunction);
                        await Log($"Registered unload for {_assetsManager.FileSystem.GetFileName(scriptFile)}");
                    }
                }
                catch (Exception e)
                {
                    await Log($"Error loading {_assetsManager.FileSystem.GetFileName(scriptFile)}: {e}", LogLevel.Error);
                }
            }
        }
        catch (Exception e)
        {
            await Log($"Error loading root scripts: {e}", LogLevel.Error);
        }
    }

    private async JobHandle Log(string message, LogLevel level = LogLevel.Debug)
    {
        await Logger.Instance.Log($"[Mod: {MetaData.Name}] {message}", level);
    }

    public void Destroy()
    {
        Unload();
        _loadFunction.Clear();
        _startFunction.Clear();
        _updateFunction.Clear();
        _loadedModules.Clear();
        _unloadFunction.Clear();
    }
}