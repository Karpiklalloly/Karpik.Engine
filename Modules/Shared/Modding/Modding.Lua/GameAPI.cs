using System.Diagnostics.CodeAnalysis;
using Karpik.Engine.Shared.Log;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

[MoonSharpUserData]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GameAPI
{
    private readonly ILogger<GameAPI> _logger;
    private readonly string _modId;
    private readonly ModContainer _container;
    
    public GameAPI(ILogger<GameAPI> logger, string modId, ModContainer container)
    {
        _logger = logger;
        _modId = modId;
        _container = container;
    }
    
    public void log(string message, LogLevel level = LogLevel.Debug)
    {
        _logger.Log(level, "[Mod {ModId}] {Message}", _modId, message);
    }

    public void print_info()
    {
        _logger.LogInformation("Mod ID: {ModId}", _modId);
    }

    // public int[] get_entities()
    // {
    //     return _world.Entities.ToArray();
    // }
    
    public void register_command(string name, Action callback)
    {
       
    }
    
    public DynValue require(string moduleName)
    {
        return _container.LoadModule(moduleName);
    }
}