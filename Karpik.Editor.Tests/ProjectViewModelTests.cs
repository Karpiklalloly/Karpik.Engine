using Karpik.Editor;
using System.Text.Json;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class ProjectViewModelTests
{
    [Fact]
    public void Path_LoadsContentTreeAndSkipsMetaFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        try
        {
            string content = Path.Combine(root, "Content", "Configs");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "player.json"), "{}");
            File.WriteAllText(Path.Combine(content, "player.json.meta"), "{}");
            string solution = Path.Combine(root, "Game.slnx");
            File.WriteAllText(solution, "");

            var viewModel = new ProjectViewModel { Path = solution };

            AssetTreeItemViewModel configs = Assert.Single(viewModel.Assets);
            Assert.Equal("Configs", configs.Name);
            Assert.True(configs.IsDirectory);
            AssetTreeItemViewModel player = Assert.Single(configs.Children);
            Assert.Equal("player.json", player.Name);
            Assert.False(player.IsDirectory);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SelectedAsset_ExposesAndSavesEditableMetaWithoutChangingAssetId()
    {
        string root = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        try
        {
            string content = Path.Combine(root, "Content");
            Directory.CreateDirectory(content);
            string source = Path.Combine(content, "player.json");
            string metaPath = source + ".meta";
            File.WriteAllText(source, "{}");
            File.WriteAllText(metaPath, """
                {"schemaVersion":1,"assetId":"b7b0fb92-a439-4d3e-9fb6-e5fc2b00b565","declaredType":"raw-json","logicalName":"game/player","targets":["Client","Server"],"importSettings":{},"dependencies":[]}
                """);
            string solution = Path.Combine(root, "Game.slnx");
            File.WriteAllText(solution, "");
            var viewModel = new ProjectViewModel { Path = solution };

            viewModel.SelectedAsset = Assert.Single(viewModel.Assets);
            AssetMetaEditorViewModel meta = Assert.IsType<AssetMetaEditorViewModel>(viewModel.SelectedMeta);
            Assert.Null(typeof(AssetMetaEditorViewModel).GetProperty(nameof(AssetMetaEditorViewModel.AssetId))!.SetMethod);
            meta.LogicalName = "game/player-server";
            meta.IncludesClient = false;
            meta.IncludesServer = true;
            meta.ImportSettingsJson = "{\"quality\":\"high\"}";
            meta.DependenciesJson = "[]";

            Assert.True(viewModel.SaveSelectedMeta());
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(metaPath));
            Assert.Equal("b7b0fb92-a439-4d3e-9fb6-e5fc2b00b565", saved.RootElement.GetProperty("assetId").GetString());
            Assert.Equal("game/player-server", saved.RootElement.GetProperty("logicalName").GetString());
            Assert.Equal(["Server"], saved.RootElement.GetProperty("targets").EnumerateArray().Select(target => target.GetString()!).ToArray());
            Assert.Equal("high", saved.RootElement.GetProperty("importSettings").GetProperty("quality").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveSelectedMeta_InvalidValueKeepsExistingFile()
    {
        string root = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        try
        {
            string content = Path.Combine(root, "Content");
            Directory.CreateDirectory(content);
            string source = Path.Combine(content, "player.json");
            string metaPath = source + ".meta";
            File.WriteAllText(source, "{}");
            string originalMeta = """
                {"schemaVersion":1,"assetId":"b7b0fb92-a439-4d3e-9fb6-e5fc2b00b565","declaredType":"raw-json","logicalName":"game/player","targets":["Client","Server"],"importSettings":{},"dependencies":[]}
                """;
            File.WriteAllText(metaPath, originalMeta);
            string solution = Path.Combine(root, "Game.slnx");
            File.WriteAllText(solution, "");
            var viewModel = new ProjectViewModel { Path = solution };

            viewModel.SelectedAsset = Assert.Single(viewModel.Assets);
            AssetMetaEditorViewModel meta = Assert.IsType<AssetMetaEditorViewModel>(viewModel.SelectedMeta);
            meta.LogicalName = "invalid";

            Assert.False(viewModel.SaveSelectedMeta());
            Assert.Equal(originalMeta, File.ReadAllText(metaPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("player.json", "raw-json", "game/Sprites/player")]
    [InlineData("player.png", "texture", "game/Sprites/player")]
    [InlineData("player.jpg", "texture", "game/Sprites/player")]
    [InlineData("player.jpeg", "texture", "game/Sprites/player")]
    [InlineData("player.font-json", "font-json", "game/Sprites/player.font")]
    [InlineData("player.vert", "shader", "game/Sprites/player.vert")]
    [InlineData("player.frag", "shader", "game/Sprites/player.frag")]
    public void Path_CreatesMissingSupportedMetaWithoutOverwritingIt(
        string fileName,
        string declaredType,
        string logicalName)
    {
        string root = Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}");
        try
        {
            string content = Path.Combine(root, "Content", "Sprites");
            Directory.CreateDirectory(content);
            string source = Path.Combine(content, fileName);
            File.WriteAllBytes(source, [1]);
            string solution = Path.Combine(root, "Game.slnx");
            File.WriteAllText(solution, "");
            var viewModel = new ProjectViewModel { Path = solution };

            string metaPath = source + ".meta";
            string firstMeta = File.ReadAllText(metaPath);
            using (JsonDocument meta = JsonDocument.Parse(firstMeta))
            {
                Assert.Equal(declaredType, meta.RootElement.GetProperty("declaredType").GetString());
                Assert.Equal(logicalName, meta.RootElement.GetProperty("logicalName").GetString());
            }

            viewModel.Path = solution;

            Assert.Equal(firstMeta, File.ReadAllText(metaPath));
            AssetTreeItemViewModel sprites = Assert.Single(viewModel.Assets);
            Assert.Equal("Sprites", sprites.Name);
            Assert.Equal(fileName, Assert.Single(sprites.Children).Name);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
