using Karpik.Editor;
using Dock.Model.Core;
using Dock.Model.ReactiveUI.Controls;
using ReactiveUI.Builder;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorWorkspaceTests
{
    [Fact]
    public void BoundedLog_DropsOldestEntries()
    {
        var log = new BoundedLog(3);

        log.Add("one");
        log.Add("two");
        log.Add("three");
        log.Add("four");

        Assert.Equal(["two", "three", "four"], log.Entries);
    }

    [Fact]
    public async Task WorkspaceStore_RestoresProjectAndLayout()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "workspace.json");
        try
        {
            var store = new WorkspaceStore(path);
            await store.SaveAsync(new EditorWorkspace
            {
                ProjectPath = @"C:\games\sample",
                LeftPanelWidth = 280,
                BottomPanelHeight = 240
            }, TestContext.Current.CancellationToken);

            EditorWorkspace restored = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(@"C:\games\sample", restored.ProjectPath);
            Assert.Equal(280, restored.LeftPanelWidth);
            Assert.Equal(240, restored.BottomPanelHeight);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void DockLayoutStore_RoundTripsDockTree()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
        string directory = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "layout.json");
        try
        {
            var tool = new Tool
            {
                Id = "hierarchy",
                Title = "Иерархия",
                Context = new ProjectViewModel { Path = directory }
            };
            var toolDock = new ToolDock
            {
                Id = "left-tools",
                Proportion = 321,
                ActiveDockable = tool,
                VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { tool }
            };
            var root = new RootDock
            {
                Id = "root",
                ActiveDockable = toolDock,
                VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { toolDock }
            };
            var store = new DockLayoutStore(path);

            store.Save(root);
            var restored = store.Load();

            Assert.NotNull(restored);
            var restoredDock = Assert.IsType<ToolDock>(Assert.Single(restored.VisibleDockables!));
            Assert.Equal(321, restoredDock.Proportion);
            Assert.Equal("hierarchy", Assert.Single(restoredDock.VisibleDockables!).Id);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
