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
    private const double MinimumToolProportion = 0.1;
    private const double MaximumToolProportion = 0.5;

    private readonly EditorShellViewModel _shell;
    private readonly EditorWorkspace _workspace;

    public ToolDock? LeftDock { get; private set; }
    public ToolDock? BottomDock { get; private set; }

    public EditorDockFactory(EditorShellViewModel shell, EditorWorkspace workspace)
    {
        _shell = shell;
        _workspace = workspace;
    }

    public override IRootDock CreateLayout()
    {
        var hierarchy = CreateTool("hierarchy", "Иерархия", _shell.Hierarchy);
        var project = CreateTool("project", "Проект", _shell.Project);
        var sessions = CreateTool("sessions", "Сессии", _shell.Sessions);
        var inspector = CreateTool("inspector", "Инспектор", _shell.Inspector);
        var console = CreateTool("console", "Консоль", _shell.Console);
        var preview = new Document
        {
            Id = "preview",
            Title = "Предпросмотр",
            Context = _shell.Preview,
            CanClose = false,
            CanFloat = false
        };

        LeftDock = new ToolDock
        {
            Id = "left-tools",
            Alignment = Alignment.Left,
            Proportion = NormalizeToolProportion(_workspace.LeftPanelWidth, DefaultLeftProportion),
            ActiveDockable = hierarchy,
            VisibleDockables = CreateList<IDockable>(project, hierarchy, sessions)
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
            ActiveDockable = preview,
            DefaultDockable = preview,
            VisibleDockables = CreateList<IDockable>(preview)
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
            Proportion = NormalizeToolProportion(_workspace.BottomPanelHeight, DefaultBottomProportion),
            ActiveDockable = console,
            VisibleDockables = CreateList<IDockable>(console)
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

    private static double NormalizeToolProportion(double value, double fallback)
    {
        if (!double.IsFinite(value) || value > 1)
        {
            return fallback;
        }

        return Math.Clamp(value, MinimumToolProportion, MaximumToolProportion);
    }
}
