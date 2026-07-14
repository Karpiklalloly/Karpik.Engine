using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.ReactiveUI.Controls;
using Karpik.Editor;
using ReactiveUI.Builder;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorDockFactoryTests
{
    static EditorDockFactoryTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    public void CreateLayout_WithLegacyPixelOrCollapsedPanelValues_UsesVisibleProportions()
    {
        using var shell = new EditorShellViewModel(
            new WorkspaceStore(Path.Combine(Path.GetTempPath(), $"karpik-editor-{Guid.NewGuid():N}.json")));
        var factory = new EditorDockFactory(
            shell,
            new EditorWorkspace
            {
                LeftPanelWidth = 0,
                BottomPanelHeight = 220
            });

        factory.CreateLayout();

        Assert.InRange(factory.LeftDock!.Proportion, 0.1, 0.5);
        Assert.InRange(factory.BottomDock!.Proportion, 0.1, 0.5);
    }

    [Fact]
    public void CreateLayout_RightInspectorUsesAValidProportion()
    {
        using var shell = new EditorShellViewModel(
            new WorkspaceStore(Path.Combine(Path.GetTempPath(), $"karpik-editor-{Guid.NewGuid():N}.json")));
        var factory = new EditorDockFactory(shell, new EditorWorkspace());

        IRootDock layout = factory.CreateLayout();

        var rightDock = Assert.IsType<ToolDock>(FindById(layout, "right-tools"));
        Assert.InRange(rightDock.Proportion, 0.1, 0.5);
    }

    [Fact]
    public void AttachContexts_NormalizesInvalidProportionsFromSavedLayout()
    {
        using var shell = new EditorShellViewModel(
            new WorkspaceStore(Path.Combine(Path.GetTempPath(), $"karpik-editor-{Guid.NewGuid():N}.json")));
        var factory = new EditorDockFactory(shell, new EditorWorkspace());
        IRootDock layout = factory.CreateLayout();
        var leftDock = Assert.IsType<ToolDock>(FindById(layout, "left-tools"));
        var bottomDock = Assert.IsType<ToolDock>(FindById(layout, "bottom-tools"));
        var rightDock = Assert.IsType<ToolDock>(FindById(layout, "right-tools"));
        leftDock.Proportion = 0;
        bottomDock.Proportion = 220;
        rightDock.Proportion = 330;

        factory.AttachContexts(layout);

        Assert.InRange(leftDock.Proportion, 0.1, 0.5);
        Assert.InRange(bottomDock.Proportion, 0.1, 0.5);
        Assert.InRange(rightDock.Proportion, 0.1, 0.5);
    }

    private static IDockable? FindById(IDockable dockable, string id)
    {
        if (dockable.Id == id)
        {
            return dockable;
        }

        if (dockable is not IDock dock || dock.VisibleDockables is null)
        {
            return null;
        }

        foreach (IDockable child in dock.VisibleDockables)
        {
            IDockable? result = FindById(child, id);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
