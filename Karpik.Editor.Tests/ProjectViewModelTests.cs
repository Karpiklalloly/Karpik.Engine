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

    [Theory]
    [InlineData("player.json", "raw-json")]
    [InlineData("player.png", "texture")]
    [InlineData("player.jpg", "texture")]
    [InlineData("player.jpeg", "texture")]
    public void Path_CreatesMissingSupportedMetaWithoutOverwritingIt(
        string fileName,
        string declaredType)
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
                Assert.Equal("game/Sprites/player", meta.RootElement.GetProperty("logicalName").GetString());
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
