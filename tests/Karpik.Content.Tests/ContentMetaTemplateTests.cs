using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class ContentMetaTemplateTests
{
    [Theory]
    [InlineData("config/player.json", "raw-json", "game/config/player")]
    [InlineData("Sprites/player.png", "texture", "game/Sprites/player")]
    [InlineData("Sprites/player.jpg", "texture", "game/Sprites/player")]
    [InlineData("Sprites/player.jpeg", "texture", "game/Sprites/player")]
    [InlineData("Fonts/default.font-json", "font-json", "game/Fonts/default.font")]
    [InlineData("Shaders/2D.vert", "shader", "game/Shaders/2D.vert")]
    [InlineData("Shaders/2D.frag", "shader", "game/Shaders/2D.frag")]
    public void TryCreate_SupportedSource_ProducesInitialMeta(
        string relativePath,
        string declaredType,
        string logicalName)
    {
        bool created = ContentMetaTemplate.TryCreate(relativePath, "game", out string metaJson);
        var diagnostics = new List<ContentDiagnostic>();

        AssetMeta meta = AssetMeta.Parse(metaJson, relativePath + ".meta", diagnostics);

        Assert.True(created);
        Assert.Empty(diagnostics);
        Assert.Equal(declaredType, meta.DeclaredType);
        Assert.Equal(logicalName, meta.LogicalName);
        Assert.Equal("{}", meta.RawImportSettingsJson);
        Assert.Empty(meta.Dependencies);
        Assert.True(AssetId.TryParse(meta.AssetId.ToCanonicalString(), out _));
    }

    [Fact]
    public void TryCreate_IncludesSharedTargets()
    {
        bool created = ContentMetaTemplate.TryCreate("config/player.json", "game", out string metaJson);
        var diagnostics = new List<ContentDiagnostic>();

        AssetMeta meta = AssetMeta.Parse(metaJson, "config/player.json.meta", diagnostics);

        Assert.True(created);
        Assert.Empty(diagnostics);
        Assert.Equal(AssetTarget.Shared, meta.Targets);
    }

    [Fact]
    public void TryCreate_UnsupportedSource_ReturnsFalse()
    {
        bool created = ContentMetaTemplate.TryCreate("Sprites/player.gif", "game", out string metaJson);

        Assert.False(created);
        Assert.Empty(metaJson);
    }
}
