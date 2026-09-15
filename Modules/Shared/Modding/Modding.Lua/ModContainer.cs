using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

public class ModContainer : IModContainer
{
    public ModMetaData? MetaData => _metaDataHandle.Asset?.MetaData;
    public IFileSystem FileSystem => _assetsManager.FileSystem;
    public string DirectoryPath { get; }
    public AssetHandle<ModMetaDataAsset> MetaDataHandle => _metaDataHandle;
    public Script Script { get; }

    public bool IsEnabled
    {
        get;
        set
        {
            if (!field && value && !_isStarted)
            {
                Start();
            }

            field = value;
        }
    } = true;

    public IReadOnlyList<DynValue> UpdateFunctions => _updateFunction;
    public IReadOnlyList<DynValue> StartFunctions => _startFunction;
    public IReadOnlyList<DynValue> LoadFunctions => _loadFunction;
    public IReadOnlyList<DynValue> UnloadFunctions => _unloadFunction;

    private readonly ILogger<ModContainer> _logger;
    private readonly IAssetsManager _assetsManager;
    private readonly Time _time;
    private readonly List<DynValue> _updateFunction = new();
    private readonly List<DynValue> _startFunction = new();
    private readonly List<DynValue> _loadFunction = new();
    private readonly List<DynValue> _unloadFunction = new();
    private readonly Dictionary<string, DynValue> _loadedModules = new();

    private bool _isStarted = false;
    private bool _disposed = false;
    private AssetHandle<ModMetaDataAsset> _metaDataHandle;
    
    internal ModContainer(ILogger<ModContainer> logger, IAssetsManager assetsManager, Time time, string directoryPath, AssetHandle<ModMetaDataAsset> metaDataHandle)
    {
        _logger = logger;
        _assetsManager = assetsManager;
        _time = time;
        DirectoryPath = directoryPath;
        _metaDataHandle = metaDataHandle;
        Script = new Script();
        Script.Options.ScriptLoader = new ModScriptLoader(directoryPath, this);
        Script.Options.DebugPrint = s => Log(s);
    }

    internal async JobHandle Initialize(ILogger<GameAPI> gameApiLogger)
    {
        Script.Globals["G"] = new GameAPI(gameApiLogger, MetaData?.Id ?? throw new NullReferenceException(), this);
        await LoadRootScripts();
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
            Log($"Error loading module {moduleName}", LogLevel.Error, ex);
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
                Log($"Update error", LogLevel.Error, e);
            }
        }
    }

    public void Start()
    {
        if (!IsEnabled) return;

        _isStarted = true;

        foreach (var func in _startFunction)
        {
            try
            {
                Script.Call(func);
            }
            catch (Exception e)
            {
                Log($"Start error", LogLevel.Error, e);
            }
        }
    }
    
    public void Load()
    {
        foreach (var func in _loadFunction)
        {
            try
            {
                Script.Call(func);
            }
            catch (Exception e)
            {
                Log($"Load error: {e}", LogLevel.Error);
            }
        }
    }
    
    public void Unload()
    {
        foreach (var func in _unloadFunction)
        {
            try
            {
                Script.Call(func);
            }
            catch (Exception e)
            {
                Log($"Unload error", LogLevel.Error, e);
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
                string fileName = _assetsManager.FileSystem.GetFileName(scriptFile);
                try
                {
                    await using var read = FileSystem.OpenRead(scriptFile);
                    Script.DoStream(read);
                    
                    var updateFunction = Script.Globals.Get(EventModMethods.OnUpdate);
                    if (updateFunction.IsNotNil() && updateFunction.Type == DataType.Function)
                    {
                        _updateFunction.Add(updateFunction);
                        Log($"Registered update for {fileName}");
                    }
                    
                    var startFunction = Script.Globals.Get(EventModMethods.OnStart);
                    if (startFunction.IsNotNil() && startFunction.Type == DataType.Function)
                    {
                        _startFunction.Add(startFunction);
                        Log($"Registered start for {fileName}");
                    }
                    
                    var loadFunction = Script.Globals.Get(EventModMethods.OnLoad);
                    if (loadFunction.IsNotNil() && loadFunction.Type == DataType.Function)
                    {
                        _loadFunction.Add(loadFunction);
                        Log($"Registered load for {fileName}");
                    }
                    
                    var unloadFunction = Script.Globals.Get(EventModMethods.OnUnload);
                    if (unloadFunction.IsNotNil() && unloadFunction.Type == DataType.Function)
                    {
                        _unloadFunction.Add(unloadFunction);
                        Log($"Registered unload for {fileName}");
                    }
                }
                catch (Exception e)
                {
                    Log($"Error loading {fileName}", LogLevel.Error, e);
                }
            }
        }
        catch (Exception e)
        {
            Log($"Error loading root scripts", LogLevel.Error, e);
        }
    }

    private void Log(string message, LogLevel level = LogLevel.Debug, Exception? ex = null)
    {
        _logger.Log(level, "[Mod: {Name}] {Message}", MetaData?.Name, message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        
        Unload();
        _loadFunction.Clear();
        _startFunction.Clear();
        _updateFunction.Clear();
        _loadedModules.Clear();
        _unloadFunction.Clear();
        
        _metaDataHandle.Dispose();
    }
}
