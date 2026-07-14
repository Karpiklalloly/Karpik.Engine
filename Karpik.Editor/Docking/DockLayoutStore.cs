using Dock.Model.Controls;
using Dock.Serializer.SystemTextJson;

namespace Karpik.Editor;

public sealed class DockLayoutStore
{
    private readonly string _path;
    private readonly DockSerializer _serializer = new();

    public DockLayoutStore(string path)
    {
        _path = path;
    }

    public IRootDock? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        using var stream = File.OpenRead(_path);
        return _serializer.Load<IRootDock>(stream);
    }

    public void Save(IRootDock layout)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _path + ".tmp";
        using (var stream = File.Create(temporaryPath))
        {
            _serializer.Save(stream, layout);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }

    public static DockLayoutStore CreateDefault()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new DockLayoutStore(Path.Combine(root, "KarpikEngine", "Editor", "layout-v2.json"));
    }
}
