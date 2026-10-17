using Karpik.Editor;
using Karpik.Engine.Core;
using ReactiveUI.Builder;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class InspectorSelectionTests
{
    static InspectorSelectionTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    private static (string directory, string solution) CreateProjectWithAsset()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"KarpikInspectorTests-{Guid.NewGuid():N}");
        string content = Path.Combine(directory, "Content");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "player.json"), "{}");
        File.WriteAllText(Path.Combine(content, "player.json.meta"), """
            {"schemaVersion":1,"assetId":"b7b0fb92-a439-4d3e-9fb6-e5fc2b00b565","declaredType":"raw-json","logicalName":"game/player","targets":["Client","Server"],"importSettings":{},"dependencies":[]}
            """);
        string solution = Path.Combine(directory, "Game.slnx");
        File.WriteAllText(solution, "");
        return (directory, solution);
    }

    [Fact]
    public void SelectingAsset_ClearsEntitySelection_AndInspectorShowsMeta()
    {
        (string directory, string solution) = CreateProjectWithAsset();
        try
        {
            using var shell = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")));
            shell.Hierarchy.Entities.Add(new EditorEntityViewModel
            {
                Id = 7,
                Components = [new EditorComponentSnapshot { TypeName = "C", DisplayValue = "v" }]
            });
            shell.Hierarchy.SelectedEntity = shell.Hierarchy.Entities[0];

            shell.Project.Path = solution;
            shell.Project.SelectedAsset = Assert.Single(shell.Project.Assets);

            Assert.Null(shell.Hierarchy.SelectedEntity);
            Assert.NotNull(shell.Inspector.SelectedMeta);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SelectingEntity_ClearsAssetSelection_AndInspectorShowsComponents()
    {
        (string directory, string solution) = CreateProjectWithAsset();
        try
        {
            using var shell = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")));
            shell.Project.Path = solution;
            shell.Project.SelectedAsset = Assert.Single(shell.Project.Assets);

            shell.Hierarchy.Entities.Add(new EditorEntityViewModel
            {
                Id = 7,
                Components = [new EditorComponentSnapshot { TypeName = "C", DisplayValue = "v" }]
            });
            shell.Hierarchy.SelectedEntity = shell.Hierarchy.Entities[0];

            Assert.Null(shell.Project.SelectedAsset);
            Assert.Null(shell.Inspector.SelectedMeta);
            Assert.Single(shell.Inspector.Components);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
