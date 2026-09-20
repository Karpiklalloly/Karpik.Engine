using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.ReactiveUI;
using Dock.Model.ReactiveUI.Controls;

namespace Karpik.Editor;

public sealed class EditorDockFactory : Factory
{
    private const double DefaultLeftProportion = 0.22;
    private const double DefaultRightProportion = 0.22;
    private const double DefaultBottomProportion = 0.28;
    private const double DebugBottomProportion = 0.36;
    private const double MinimumToolProportion = 0.1;
    private const double MaximumToolProportion = 0.5;
    private const string SceneContext = "Редактирование сцены пока недоступно.";
    private const string GameContext = "Встроенный Game View пока недоступен.";
    private static readonly string[] RequiredDockIds =
    [
        "root", "workspace", "main-row", "left-tools", "right-tools", "bottom-tools",
        "documents", "hierarchy", "sessions", "project", "inspector", "console",
        "scene", "game", "preview"
    ];

    private readonly EditorShellViewModel _shell;

    public ToolDock? LeftDock { get; private set; }
    public ToolDock? BottomDock { get; private set; }

    public EditorDockFactory(EditorShellViewModel shell, EditorWorkspace workspace)
    {
        _shell = shell;
    }

    public override IRootDock CreateLayout() => CreateLayout(EditorLayoutPreset.Unity);

    public IRootDock CreateLayout(EditorLayoutPreset preset)
    {
        var hierarchy = CreateTool("hierarchy", "Иерархия", _shell.Hierarchy);
        var project = CreateTool("project", "Проект", _shell.Project);
        var sessions = CreateTool("sessions", "Сессии", _shell.Sessions);
        var inspector = CreateTool("inspector", "Инспектор", _shell.Inspector);
        var console = CreateTool("console", "Консоль", _shell.Console);
        var scene = CreateDocument("scene", "Сцена", SceneContext);
        var game = CreateDocument("game", "Игра", GameContext);
        var preview = CreateDocument("preview", "Предпросмотр", _shell.Preview);

        LeftDock = new ToolDock
        {
            Id = "left-tools",
            Alignment = Alignment.Left,
            Proportion = DefaultLeftProportion,
            ActiveDockable = preset == EditorLayoutPreset.Debug ? sessions : hierarchy,
            VisibleDockables = CreateList<IDockable>(hierarchy, sessions)
        };
        var inspectorDock = new ToolDock
        {
            Id = "right-tools",
            Alignment = Alignment.Right,
            Proportion = DefaultRightProportion,
            ActiveDockable = inspector,
            VisibleDockables = CreateList<IDockable>(inspector)
        };
        var documentDock = new DocumentDock
        {
            Id = "documents",
            IsCollapsable = false,
            ActiveDockable = scene,
            DefaultDockable = scene,
            VisibleDockables = CreateList<IDockable>(scene, game, preview)
        };
        var horizontal = new ProportionalDock
        {
            Id = "main-row",
            Orientation = Orientation.Horizontal,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(
                LeftDock,
                new ProportionalDockSplitter(),
                documentDock,
                new ProportionalDockSplitter(),
                inspectorDock)
        };

        BottomDock = new ToolDock
        {
            Id = "bottom-tools",
            Alignment = Alignment.Bottom,
            Proportion = preset == EditorLayoutPreset.Debug ? DebugBottomProportion : DefaultBottomProportion,
            ActiveDockable = preset == EditorLayoutPreset.Debug ? console : project,
            VisibleDockables = CreateList<IDockable>(project, console)
        };
        var vertical = new ProportionalDock
        {
            Id = "workspace",
            Orientation = Orientation.Vertical,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(
                horizontal,
                new ProportionalDockSplitter(),
                BottomDock)
        };
        var root = new RootDock
        {
            Id = "root",
            IsCollapsable = false,
            ActiveDockable = vertical,
            DefaultDockable = vertical,
            VisibleDockables = CreateList<IDockable>(vertical)
        };

        return root;
    }

    public static bool IsValidLayout(IRootDock? layout)
    {
        if (layout is null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return VisitForValidation(layout, seen)
               && RequiredDockIds.All(seen.Contains);
    }

    private static bool VisitForValidation(IDockable dockable, HashSet<string> seen)
    {
        if (dockable is ProportionalDockSplitter)
        {
            return true;
        }

        string? id = dockable.Id;
        if (string.IsNullOrWhiteSpace(id)
            || !RequiredDockIds.Contains(id, StringComparer.Ordinal)
            || !seen.Add(id)
            || !HasExpectedType(dockable, id))
        {
            return false;
        }

        if (dockable is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (IDockable child in children)
            {
                if (!VisitForValidation(child, seen))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasExpectedType(IDockable dockable, string id) => id switch
    {
        "root" => dockable is RootDock,
        "workspace" or "main-row" => dockable is ProportionalDock,
        "left-tools" or "right-tools" or "bottom-tools" => dockable is ToolDock,
        "documents" => dockable is DocumentDock,
        "hierarchy" or "sessions" or "project" or "inspector" or "console" => dockable is Tool,
        "scene" or "game" or "preview" => dockable is Document,
        _ => false
    };

    public void AttachContexts(IRootDock layout)
    {
        Visit(layout);
    }

    private void Visit(IDockable dockable)
    {
        dockable.Context = dockable.Id switch
        {
            "project" => _shell.Project,
            "hierarchy" => _shell.Hierarchy,
            "sessions" => _shell.Sessions,
            "inspector" => _shell.Inspector,
            "console" => _shell.Console,
            "scene" => SceneContext,
            "game" => GameContext,
            "preview" => _shell.Preview,
            _ => dockable.Context
        };

        if (dockable is ToolDock toolDock)
        {
            if (toolDock.Id == "left-tools")
            {
                toolDock.Proportion = NormalizeToolProportion(toolDock.Proportion, DefaultLeftProportion);
                LeftDock = toolDock;
            }
            else if (toolDock.Id == "bottom-tools")
            {
                toolDock.Proportion = NormalizeToolProportion(toolDock.Proportion, DefaultBottomProportion);
                BottomDock = toolDock;
            }
            else if (toolDock.Id == "right-tools")
            {
                toolDock.Proportion = NormalizeToolProportion(toolDock.Proportion, DefaultRightProportion);
            }
        }

        if (dockable is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (IDockable child in children)
            {
                Visit(child);
            }
        }
    }

    private static Tool CreateTool(string id, string title, object context) => new()
    {
        Id = id,
        Title = title,
        Context = context,
        CanClose = false
    };

    private static Document CreateDocument(string id, string title, object context) => new()
    {
        Id = id,
        Title = title,
        Context = context,
        CanClose = false,
        CanFloat = false
    };

    private static double NormalizeToolProportion(double value, double fallback)
    {
        if (!double.IsFinite(value) || value > 1)
        {
            return fallback;
        }

        return Math.Clamp(value, MinimumToolProportion, MaximumToolProportion);
    }
}
