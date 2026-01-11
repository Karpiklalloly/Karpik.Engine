using Karpik.Engine.Shared.AssetManagement.Base;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace Karpik.Engine.Shared.Modding.Lua;

public class ModScriptLoader : ScriptLoaderBase
{
    public IFileSystem FileSystem => _container.FileSystem;
    
    private readonly string _modBasePath;
    private readonly ModContainer _container;

    public ModScriptLoader(string modBasePath, ModContainer container)
    {
        _modBasePath = modBasePath;
        _container = container;
        ModulePaths = ["?.lua", "?/init.lua"];
    }

    public override object LoadFile(string file, Table globalContext)
    {
        string path = FileSystem.Combine(_modBasePath, file);
        return FileSystem.Exists(path) ? FileSystem.OpenRead(path) : null;
    }

    public override string ResolveFileName(string filename, Table globalContext)
    {
        return FileSystem.Combine(_modBasePath, filename);
    }

    public override string ResolveModuleName(string modname, Table globalContext)
    {
        return modname.Replace('.', FileSystem.DirectorySeparatorChar) + ".lua";
    }

    public override bool ScriptFileExists(string name)
    {
        return FileSystem.Exists(FileSystem.Combine(_modBasePath, name));
    }
}