using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Jobs;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IScriptLoader))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class LuaScriptLoader(IFileSystem fileSystem) : IScriptLoader
{
    public async JobHandle<IScriptRuntime> Load(ModDefinition definition, ExecutionSide side)
    {
        string sideDirectory = side switch
        {
            ExecutionSide.Client => "Client",
            ExecutionSide.Server => "Server",
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };

        var script = new Script(CoreModules.Preset_SoftSandbox);
        script.Options.ScriptLoader = new ModScriptLoader(definition.Directory, fileSystem, side);
        
        var entryPoints = new List<Table>(2);
        
        LoadEntryPoint("Shared");
        LoadEntryPoint(sideDirectory);
        
        if (entryPoints.Count == 0)
        {
            throw new FileNotFoundException(
                $"Mod '{definition.MetaData.Id}' has no main.lua entry point.");
        }
        
        return new LuaScriptRuntime(script, entryPoints.ToArray());
        
        void LoadEntryPoint(string directory)
        {
            string path = fileSystem.Combine(definition.Directory, directory, "main.lua");

            if (!fileSystem.Exists(path))
                return;

            using Stream stream = fileSystem.OpenRead(path);
            DynValue result = script.DoStream(stream);

            entryPoints.Add(ReadCallbacks(result, path));
        }
    }
    
    internal static Table ReadCallbacks(DynValue result, string path)
    {
        if (result.Type != DataType.Table)
        {
            throw new InvalidDataException(
                $"Entry point '{path}' must return a callbacks table.");
        }

        string[] names =
        [
            "on_load",
            "on_start",
            "on_update",
            "on_fixed_update",
            "on_destroy",
            "on_unload"
        ];

        foreach (string name in names)
        {
            DynValue callback = result.Table.Get(name);

            if (!callback.IsNil() && callback.Type != DataType.Function)
            {
                throw new InvalidDataException(
                    $"Callback '{name}' in '{path}' must be a Lua function.");
            }
        }

        return result.Table;
    }
}