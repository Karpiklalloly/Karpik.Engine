using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.ReactiveUI.Controls;
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
        IRootDock layout = _serializer.Load<IRootDock>(stream);
        if (_legacyPath is not null
            && string.Equals(path, _legacyPath, StringComparison.OrdinalIgnoreCase))
        {
            UpgradeLegacyLayout(layout);
        }

        return layout;
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

    private static void UpgradeLegacyLayout(IRootDock layout)
    {
        VisitLegacyLayout(layout, new HashSet<IDockable>(ReferenceEqualityComparer.Instance));
    }

    private static void VisitLegacyLayout(IDockable dockable, HashSet<IDockable> visited)
    {
        if (!visited.Add(dockable))
        {
            return;
        }

        if (dockable is DocumentDock documents)
        {
            UpgradeLegacyDocuments(documents);
        }

        if (dockable is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (IDockable child in children)
            {
                VisitLegacyLayout(child, visited);
            }
        }

        if (dockable is IRootDock root)
        {
            VisitLegacyCollection(root.HiddenDockables, visited);
            VisitLegacyCollection(root.LeftPinnedDockables, visited);
            VisitLegacyCollection(root.RightPinnedDockables, visited);
            VisitLegacyCollection(root.TopPinnedDockables, visited);
            VisitLegacyCollection(root.BottomPinnedDockables, visited);

            if (root.PinnedDock is { } pinned)
            {
                VisitLegacyLayout(pinned, visited);
            }

            if (root.Windows is { } windows)
            {
                foreach (IDockWindow window in windows)
                {
                    if (window.Layout is { } floatingLayout)
                    {
                        VisitLegacyLayout(floatingLayout, visited);
                    }
                }
            }
        }
    }

    private static void VisitLegacyCollection(
        IList<IDockable>? dockables,
        HashSet<IDockable> visited)
    {
        if (dockables is null)
        {
            return;
        }

        foreach (IDockable dockable in dockables)
        {
            VisitLegacyLayout(dockable, visited);
        }
    }

    private static void UpgradeLegacyDocuments(DocumentDock documents)
    {
        if (documents.VisibleDockables is not { } dockables)
        {
            return;
        }

        int previewIndex = -1;
        bool hasScene = false;
        bool hasGame = false;
        foreach (IDockable dockable in dockables)
        {
            hasScene |= dockable.Id == "scene";
            hasGame |= dockable.Id == "game";
            if (dockable.Id == "preview")
            {
                previewIndex = dockables.IndexOf(dockable);
            }
        }

        if (previewIndex < 0)
        {
            return;
        }

        if (!hasScene)
        {
            dockables.Insert(previewIndex++, CreateLegacyDocument("scene", "Сцена"));
        }

        if (!hasGame)
        {
            dockables.Insert(previewIndex, CreateLegacyDocument("game", "Игра"));
        }
    }

    private static Document CreateLegacyDocument(string id, string title) => new()
    {
        Id = id,
        Title = title,
        CanClose = false,
        CanFloat = false
    };

    private static string GetEditorDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KarpikEngine",
        "Editor");
}
