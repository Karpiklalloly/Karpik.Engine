using Karpik.Engine.Core.FileSystem;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace Karpik.Engine.Shared.Modding.Lua;

public class ModScriptLoader : ScriptLoaderBase
{
    private readonly string _root;
    private readonly string _side;
    private readonly IFileSystem _fileSystem;

    public ModScriptLoader(string root, IFileSystem fileSystem, ExecutionSide side)
    {
        _root = root;
        _fileSystem = fileSystem;
        _side = side switch
        {
            ExecutionSide.Client => "Client",
            ExecutionSide.Server => "Server",
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };

        IgnoreLuaPathGlobal = true;
        ModulePaths =
        [
            "Shared/?.lua",
            "Shared/?/init.lua",
            $"{_side}/?.lua",
            $"{_side}/?/init.lua"
        ];
    }

    public override object LoadFile(string file, Table globalContext)
    {
        using Stream stream = _fileSystem.OpenRead(ResolvePath(file));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public override bool ScriptFileExists(string name) => _fileSystem.Exists(ResolvePath(name));

    private string ResolvePath(string file)
    {
        string relative = file.Replace('\\', '/');
        string[] parts = relative.Split('/');

        if (_fileSystem.IsPathRooted(relative)
            || relative.Contains(':')
            || parts.Any(part => part is "" or "." or "..")
            || (parts[0] != "Shared" && parts[0] != _side))
        {
            throw new InvalidOperationException($"Script path is not allowed: '{file}'.");
        }

        return _fileSystem.Combine(_root, relative);
    }
}
