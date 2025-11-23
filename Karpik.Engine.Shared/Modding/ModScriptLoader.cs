using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace Karpik.Engine.Shared.Modding;

public class ModScriptLoader : ScriptLoaderBase
{
    private readonly string _modBasePath;

    public ModScriptLoader(string modBasePath)
    {
        _modBasePath = modBasePath;
        ModulePaths = new[] { "?.lua", "?/init.lua" };
    }

    public override object LoadFile(string file, Table globalContext)
    {
        string path = Path.Combine(_modBasePath, file);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public override string ResolveFileName(string filename, Table globalContext)
    {
        return Path.Combine(_modBasePath, filename);
    }

    public override string ResolveModuleName(string modname, Table globalContext)
    {
        return modname.Replace('.', Path.DirectorySeparatorChar) + ".lua";
    }

    public override bool ScriptFileExists(string name)
    {
        return File.Exists(Path.Combine(_modBasePath, name));
    }
}