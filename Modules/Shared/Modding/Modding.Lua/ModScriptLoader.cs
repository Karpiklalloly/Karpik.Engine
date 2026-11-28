using Karpik.Engine.Core.FileSystem;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace Karpik.Engine.Shared.Modding.Lua;

public class ModScriptLoader : ScriptLoaderBase
{
    private readonly ModScriptSource? _source;
    private readonly string _side;
    private readonly string? _legacyRoot;
    private readonly IFileSystem? _legacyFileSystem;

    public ModScriptLoader(string root, IFileSystem fileSystem, ExecutionSide side) : this(side)
    {
        if (LuaSourceCapture.IsSupported) _source = LuaSourceCapture.Capture(root, side);
        else (_legacyRoot, _legacyFileSystem) = (root, fileSystem);
    }

    internal ModScriptLoader(ModScriptSource source) : this(source.Side) => _source = source;

    private ModScriptLoader(ExecutionSide side)
    {
        _side = LuaSourceCapture.SideDirectory(side);
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
        string path = ResolvePath(file);
        if (_source is not null)
            return _source.TryGetFile(path, out var text) ? text! :
                throw new FileNotFoundException($"Script was not present in the captured source: '{file}'.", file);

        using var stream = _legacyFileSystem!.OpenRead(_legacyFileSystem.Combine(_legacyRoot!, path));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public override bool ScriptFileExists(string name)
    {
        string path = ResolvePath(name);
        return _source is not null ? _source.TryGetFile(path, out _) :
            _legacyFileSystem!.Exists(_legacyFileSystem.Combine(_legacyRoot!, path));
    }

    private string ResolvePath(string file)
    {
        string relative = file.Replace('\\', '/');
        string[] parts = relative.Split('/');
        if (relative.Contains(':') || parts.Any(part => part is "" or "." or "..") ||
            (parts[0] != "Shared" && parts[0] != _side))
            throw new InvalidOperationException($"Script path is not allowed: '{file}'.");
        return relative;
    }
}
