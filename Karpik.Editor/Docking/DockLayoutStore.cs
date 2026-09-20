using Dock.Model.Controls;
using Dock.Serializer.SystemTextJson;

namespace Karpik.Editor;

public sealed class DockLayoutStore
{
    private readonly string _path;
    private readonly string? _legacyPath;
    private readonly DockSerializer _serializer = new();

    public DockLayoutStore(string path)
        : this(path, null)
    {
    }

    private DockLayoutStore(string path, string? legacyPath)
    {
        _path = path;
        _legacyPath = legacyPath;
    }

    public IRootDock? Load()
    {
        string? path = File.Exists(_path)
            ? _path
            : _legacyPath is not null && File.Exists(_legacyPath) ? _legacyPath : null;
        if (path is null)
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return _serializer.Load<IRootDock>(stream);
    }

    public bool Exists() => File.Exists(_path)
                            || (_legacyPath is not null && File.Exists(_legacyPath));

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

    public static DockLayoutStore CreateDefault() => CreateCurrent();

    public static DockLayoutStore CreateCurrent(string? editorDirectory = null) =>
        CreateMigrating(editorDirectory ?? GetEditorDirectory(), "layout-current-v2.json");

    public static DockLayoutStore CreateCustom(string? editorDirectory = null) =>
        CreateMigrating(editorDirectory ?? GetEditorDirectory(), "layout-custom-v2.json");

    private static DockLayoutStore CreateMigrating(string directory, string fileName)
    {
        return new DockLayoutStore(
            Path.Combine(directory, fileName),
            Path.Combine(directory, "layout-v2.json"));
    }

    private static string GetEditorDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KarpikEngine",
        "Editor");
}
