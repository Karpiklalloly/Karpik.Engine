using Karpik.Editor;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.ReactiveUI.Controls;
using ReactiveUI.Builder;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorWorkspaceTests
{
    [Fact]
    public void EditorSettings_ApplyUsesSelectedValuesAndCancelDoesNotApply()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
        int applyCount = 0;
        EditorUiDensity appliedDensity = default;
        EditorLayoutPreset appliedPreset = default;
        var viewModel = new EditorSettingsViewModel(
            EditorUiDensity.Compact,
            EditorLayoutPreset.Unity,
            (density, preset) =>
            {
                applyCount++;
                appliedDensity = density;
                appliedPreset = preset;
            },
            () => { });
        viewModel.UiDensity = EditorUiDensity.UltraCompact;
        viewModel.LayoutPreset = EditorLayoutPreset.Debug;

        using (viewModel.ApplyCommand.Execute().Subscribe()) { }
        using (viewModel.CancelCommand.Execute().Subscribe()) { }

        Assert.Equal(1, applyCount);
        Assert.Equal(EditorUiDensity.UltraCompact, appliedDensity);
        Assert.Equal(EditorLayoutPreset.Debug, appliedPreset);
    }

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
    public async Task WorkspaceStore_RestoresSolutionAndLayout()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "workspace.json");
        try
        {
            var store = new WorkspaceStore(path);
            await store.SaveAsync(new EditorWorkspace
            {
                SolutionPath = @"C:\games\sample\Sample.slnx",
                LeftPanelWidth = 280,
                BottomPanelHeight = 240
            }, TestContext.Current.CancellationToken);

            EditorWorkspace restored = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(@"C:\games\sample\Sample.slnx", restored.SolutionPath);
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
    public async Task WorkspaceStore_RoundTripsDensityAndLayoutPreset()
    {
        string path = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}", "workspace.json");
        var store = new WorkspaceStore(path);

        await store.SaveAsync(new EditorWorkspace
        {
            UiDensity = EditorUiDensity.UltraCompact,
            LayoutPreset = EditorLayoutPreset.Custom
        }, TestContext.Current.CancellationToken);

        EditorWorkspace restored = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EditorUiDensity.UltraCompact, restored.UiDensity);
        Assert.Equal(EditorLayoutPreset.Custom, restored.LayoutPreset);
    }

    [Fact]
    public async Task WorkspaceStore_LegacyWorkspaceUsesNewDefaults()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "workspace.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "{\"SolutionPath\":\"C:\\\\games\\\\sample\\\\Sample.slnx\"}");

        try
        {
            EditorWorkspace restored = await new WorkspaceStore(path).LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(EditorUiDensity.Compact, restored.UiDensity);
            Assert.Equal(EditorLayoutPreset.Unity, restored.LayoutPreset);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
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

    [Fact]
    public void DockLayoutStore_SavesInitializedDockTree()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
        string directory = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "layout.json");
        try
        {
            using var shell = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")));
            var factory = new EditorDockFactory(shell, new EditorWorkspace());
            IRootDock layout = factory.CreateLayout();
            factory.InitLayout(layout);
            var store = new DockLayoutStore(path);

            store.Save(layout);

            Assert.NotNull(store.Load());
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
    public void DockLayoutStore_CustomTreeIsUnchangedByPresetGeneration()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
        string directory = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        try
        {
            var scene = new Document { Id = "scene" };
            var game = new Document { Id = "game" };
            var documents = new DocumentDock
            {
                Id = "documents",
                Proportion = 0.63,
                ActiveDockable = game,
                VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable>
                {
                    game,
                    scene
                }
            };
            var root = new RootDock
            {
                Id = "root",
                ActiveDockable = documents,
                VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { documents }
            };
            var store = DockLayoutStore.CreateCustom(directory);
            store.Save(root);

            using var shell = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")));
            new EditorDockFactory(shell, new EditorWorkspace()).CreateLayout(EditorLayoutPreset.Unity);

            IRootDock restored = Assert.IsAssignableFrom<IRootDock>(store.Load());
            var restoredDocuments = Assert.IsType<DocumentDock>(Assert.Single(restored.VisibleDockables!));
            Assert.Equal(["game", "scene"], restoredDocuments.VisibleDockables!.Select(x => x.Id));
            Assert.Equal(0.63, restoredDocuments.Proportion);
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
    public void DockLayoutStore_CurrentAndCustomTreesRemainSeparate()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
        string directory = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        try
        {
            var customDock = new DocumentDock
            {
                Id = "custom-documents",
                Proportion = 0.63,
                VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable>
                {
                    new Document { Id = "custom" }
                }
            };
            var currentDock = new DocumentDock
            {
                Id = "current-documents",
                Proportion = 0.37,
                VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable>
                {
                    new Document { Id = "current" }
                }
            };
            var customStore = new DockLayoutStore(Path.Combine(directory, "layout-custom-v2.json"));
            var currentStore = new DockLayoutStore(Path.Combine(directory, "layout-current-v2.json"));

            customStore.Save(new RootDock
            {
                Id = "custom-root",
                ActiveDockable = customDock,
                VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { customDock }
            });
            currentStore.Save(new RootDock
            {
                Id = "current-root",
                ActiveDockable = currentDock,
                VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { currentDock }
            });

            Assert.Equal("custom-root", customStore.Load()!.Id);
            Assert.Equal("current-root", currentStore.Load()!.Id);
            Assert.Equal(0.63, Assert.IsType<DocumentDock>(Assert.Single(customStore.Load()!.VisibleDockables!)).Proportion);
            Assert.Equal(0.37, Assert.IsType<DocumentDock>(Assert.Single(currentStore.Load()!.VisibleDockables!)).Proportion);
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
    public void DockLayoutStore_FactoryPathsLoadLegacyLayoutWithoutTouchingUserProfile()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
        Assert.NotNull(DockLayoutStore.CreateCurrent());
        Assert.NotNull(DockLayoutStore.CreateCustom());

        string directory = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        try
        {
            var documents = new DocumentDock
            {
                Id = "documents",
                Proportion = 0.61,
                VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable>
                {
                    new Document { Id = "game" },
                    new Document { Id = "scene" }
                }
            };
            var legacyRoot = new RootDock
            {
                Id = "legacy-root",
                ActiveDockable = documents,
                VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { documents }
            };
            new DockLayoutStore(Path.Combine(directory, "layout-v2.json")).Save(legacyRoot);

            IRootDock current = Assert.IsAssignableFrom<IRootDock>(
                DockLayoutStore.CreateCurrent(directory).Load());
            IRootDock custom = Assert.IsAssignableFrom<IRootDock>(
                DockLayoutStore.CreateCustom(directory).Load());

            Assert.Equal("legacy-root", current.Id);
            var customDocuments = Assert.IsType<DocumentDock>(Assert.Single(custom.VisibleDockables!));
            Assert.Equal(["game", "scene"], customDocuments.VisibleDockables!.Select(x => x.Id));
            Assert.Equal(0.61, customDocuments.Proportion);
            Assert.True(File.Exists(Path.Combine(directory, "layout-v2.json")));
            Assert.False(File.Exists(Path.Combine(directory, "layout-current-v2.json")));
            Assert.False(File.Exists(Path.Combine(directory, "layout-custom-v2.json")));
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
