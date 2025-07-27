using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding;

public class ModContainer
{
    public string DirectoryPath { get; }
    public ModMetaData MetaData { get; }
    public Script Script { get; }
    public bool IsEnabled { get; set; } = true;
    public List<DynValue> UpdateFunction { get; private set; } = new();
    public List<DynValue> DebugFunction { get; private set; } = new();
    public List<DynValue> FixedUpdateFunction { get; private set; } = new(); 
    public List<DynValue> StartFunction { get; private set; } = new();
    public List<DynValue> LoadFunction { get; private set; } = new();
    public List<DynValue> UnloadFunction { get; private set; } = new();
    
    private readonly Dictionary<string, DynValue> _loadedModules = new();
    
    public ModContainer(string directoryPath, ModMetaData metaData)
    {
        DirectoryPath = directoryPath;
        MetaData = metaData;
        Script = new Script();
        Script.Options.ScriptLoader = new ModScriptLoader(directoryPath);
        Script.Options.DebugPrint = s => Log(s);
    }

    public void Initialize()
    {
        UserData.RegisterType<GameAPI>();
        Script.Globals["G"] = new GameAPI(MetaData.Id, this);
        LoadRootScripts();
    }

    public DynValue LoadModule(string moduleName)
    {
        if (_loadedModules.TryGetValue(moduleName, out var module))
            return module;
        
        try
        {
            string path = moduleName.Replace('.', Path.DirectorySeparatorChar);
            if (!path.EndsWith(".lua")) path += ".lua";
            
            DynValue result = Script.DoFile(path);
            _loadedModules[moduleName] = result;
            return result;
        }
        catch (Exception ex)
        {
            Log($"Error loading module {moduleName}: {ex.Message}", LogLevel.Error);
            return DynValue.Nil;
        }
    }

    public void Update()
    {
        foreach (var func in UpdateFunction)
        {
            try
            {
                Script.Call(func, Time.DeltaTime);
            }
            catch (Exception e)
            {
                Log($"Update error: {e.Message}", LogLevel.Error);
            }
        }
    }
    
    public void DebugUpdate()
    {
        foreach (var func in DebugFunction)
        {
            try
            {
                Script.Call(func, Time.DeltaTime);
            }
            catch (Exception e)
            {
                Log($"Update error: {e.Message}", LogLevel.Error);
            }
        }
    }
    
    public void FixedUpdate()
    {
        foreach (var func in FixedUpdateFunction)
        {
            try
            {
                Script.Call(func, Time.DeltaTime);
            }
            catch (Exception e)
            {
                Log($"Fixed update error: {e.Message}", LogLevel.Error);
            }
        }
    }

    public void Start()
    {
        foreach (var func in StartFunction)
        {
            try
            {
                Script.Call(func);
            }
            catch (Exception e)
            {
                Log($"Start error: {e.Message}", LogLevel.Error);
            }
        }
    }
    
    public void Load()
    {
        foreach (var func in LoadFunction)
        {
            try
            {
                Script.Call(func);
            }
            catch (Exception e)
            {
                Log($"Load error: {e.Message}", LogLevel.Error);
            }
        }
    }
    
    public void Unload()
    {
        foreach (var func in UnloadFunction)
        {
            try
            {
                Script.Call(func);
            }
            catch (Exception e)
            {
                Log($"Unload error: {e.Message}", LogLevel.Error);
            }
        }
    }

    private void LoadRootScripts()
    {
        try
        {
            var rootScripts = Directory.GetFiles(DirectoryPath, "*.lua", SearchOption.TopDirectoryOnly);

            foreach (var scriptFile in rootScripts)
            {
                try
                {
                    Script.DoFile(scriptFile);
                    
                    var updateFunction = Script.Globals.Get(EventModMethods.OnUpdate);
                    if (updateFunction.IsNotNil() && updateFunction.Type == DataType.Function)
                    {
                        UpdateFunction.Add(updateFunction);
                        Log($"Registered update for {Path.GetFileName(scriptFile)}");
                    }
                    
                    var debugUpdateFunction = Script.Globals.Get(EventModMethods.OnDebugUpdate);
                    if (debugUpdateFunction.IsNotNil() && debugUpdateFunction.Type == DataType.Function)
                    {
                        DebugFunction.Add(debugUpdateFunction);
                        Log($"Registered debug update for {Path.GetFileName(scriptFile)}");
                    }
                    
                    var fixedUpdateFunction = Script.Globals.Get(EventModMethods.OnFixedUpdate);
                    if (fixedUpdateFunction.IsNotNil() && fixedUpdateFunction.Type == DataType.Function)
                    {
                        FixedUpdateFunction.Add(fixedUpdateFunction);
                        Log($"Registered fixed update for {Path.GetFileName(scriptFile)}");
                    }
                    
                    var startFunction = Script.Globals.Get(EventModMethods.OnStart);
                    if (startFunction.IsNotNil() && startFunction.Type == DataType.Function)
                    {
                        StartFunction.Add(startFunction);
                        Log($"Registered start for {Path.GetFileName(scriptFile)}");
                    }
                    
                    var loadFunction = Script.Globals.Get(EventModMethods.OnLoad);
                    if (loadFunction.IsNotNil() && loadFunction.Type == DataType.Function)
                    {
                        LoadFunction.Add(loadFunction);
                        Log($"Registered load for {Path.GetFileName(scriptFile)}");
                    }
                    
                    var unloadFunction = Script.Globals.Get(EventModMethods.OnUnload);
                    if (unloadFunction.IsNotNil() && unloadFunction.Type == DataType.Function)
                    {
                        UnloadFunction.Add(unloadFunction);
                        Log($"Registered unload for {Path.GetFileName(scriptFile)}");
                    }
                }
                catch (Exception e)
                {
                    Log($"Error loading {Path.GetFileName(scriptFile)}: {e.Message}", LogLevel.Error);
                }
            }
        }
        catch (Exception e)
        {
            Log($"Error loading root scripts: {e.Message}", LogLevel.Error);
        }
    }

    private void Log(string message, LogLevel level = LogLevel.Debug)
    {
        Logger.Instance.Log($"[Mod: {MetaData.Name}] {message}", level);
    }


}