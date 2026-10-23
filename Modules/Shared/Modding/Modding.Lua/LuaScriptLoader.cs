using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IScriptLoader))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class LuaScriptLoader(
    IFileSystem fileSystem,
    ILogger<IScriptLoader> logger) : IScriptLoader
{
    public async JobHandle<IScriptRuntime> Load(ModDefinition definition, ExecutionSide side)
    {
        string sideDirectory = side switch
        {
            ExecutionSide.Client => "Client",
            ExecutionSide.Server => "Server",
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };

        var script = new Script(CoreModules.Preset_SoftSandbox | CoreModules.LoadMethods);
        script.Options.DebugPrint = message =>
        {
            logger.LogInformation("[Mod: {mod_id}] {Message}", definition.MetaData.Id, message);
        };
        script.Options.ScriptLoader = new ModScriptLoader(definition.Directory, fileSystem, side);
        
        var entryPoints = new List<Table>(2);
        var entryPointPaths = new List<string>(2);
        
        LoadEntryPoint("Shared");
        LoadEntryPoint(sideDirectory);
        
        if (entryPoints.Count == 0)
        {
            logger.LogWarning("Mod '{Id}' has no main.lua entry point.", definition.MetaData.Id);
        }
        
        return new LuaScriptRuntime(script, entryPoints.ToArray(), definition.MetaData.Id, entryPointPaths.ToArray());
        
        void LoadEntryPoint(string directory)
        {
            string path = fileSystem.Combine(definition.Directory, directory, "main.lua");

            if (!fileSystem.Exists(path))
                return;

            try
            {
                using Stream stream = fileSystem.OpenRead(path);

                DynValue result = script.DoStream(
                    stream, codeFriendlyName: path);

                entryPoints.Add(ReadCallbacks(result, path));
                entryPointPaths.Add(path);
            }
            catch (Exception error)
            {
                string diagnostic = error is InterpreterException luaError
                    ? luaError.DecoratedMessage
                    : error.Message;

                logger.LogError(
                    error,
                    "[Mod: {ModId}] Failed to prepare {Path}: {Diagnostic}",
                    definition.MetaData.Id,
                    path,
                    diagnostic);

                throw;
            }
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
