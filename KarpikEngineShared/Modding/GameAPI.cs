using System.Diagnostics.CodeAnalysis;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding;

[MoonSharpUserData]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GameAPI
{
    private readonly string _modId;
    private readonly ModContainer _container;

    public GameAPI(string modId, ModContainer container)
    {
        _modId = modId;
        _container = container;
    }
    
    public void log(string message, LogLevel level = LogLevel.Debug)
    {
        Logger.Instance.Log(_modId, message, level);
    }

    public void print_info()
    {
        Logger.Instance.Log($"Mod ID: {_modId}", LogLevel.Info);
    }
    
    public void register_command(string name, Action callback)
    {
       
    }
    
    public DynValue require(string moduleName)
    {
        return _container.LoadModule(moduleName);
    }
}