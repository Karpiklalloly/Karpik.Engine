using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class TextureContentBuildTests
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL4uQAAAABJRU5ErkJggg==");

    [Fact]
    public void Build_MixedSupportedSources_PublishesSourceArtifacts()
    {
        using var temporary = new TemporaryDirectory();
        string sourceRoot = temporary.CreateSubdirectory("source");
        string outputRoot = temporary.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(sourceRoot, "config/player.json", "{}", logicalName: "game/config/player");
        string imagePath = Path.Combine(sourceRoot, "Sprites", "player.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        File.WriteAllBytes(imagePath, ValidPng);
        Assert.True(ContentMetaTemplate.TryCreate("Sprites/player.png", "game", out string imageMeta));
        File.WriteAllText(imagePath + ".meta", imageMeta);
        string fontPath = Path.Combine(sourceRoot, "Fonts", "default.font-json");
        Directory.CreateDirectory(Path.GetDirectoryName(fontPath)!);
        byte[] fontBytes = "{\"atlas\":{\"type\":\"msdf\"}}"u8.ToArray();
        File.WriteAllBytes(fontPath, fontBytes);
        Assert.True(ContentMetaTemplate.TryCreate("Fonts/default.font-json", "game", out string fontMeta));
        File.WriteAllText(fontPath + ".meta", fontMeta);
        string shaderPath = Path.Combine(sourceRoot, "Shaders", "main.vert");
        Directory.CreateDirectory(Path.GetDirectoryName(shaderPath)!);
        byte[] shaderBytes = "#version 450\nvoid main() {}"u8.ToArray();
        File.WriteAllBytes(shaderPath, shaderBytes);
        Assert.True(ContentMetaTemplate.TryCreate("Shaders/main.vert", "game", out string shaderMeta));
        File.WriteAllText(shaderPath + ".meta", shaderMeta);

        ContentBuildResult result = new ContentBuildCoordinator().Build(new ContentBuildOptions
        {
            SourceRoot = sourceRoot,
            OutputRoot = outputRoot,
            Namespace = "game"
        });

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        ContentManifestEntry texture = Assert.Single(result.Manifest!.Entries, entry => entry.DeclaredType == "texture");
        string artifactPath = Path.Combine(outputRoot, texture.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(ValidPng, File.ReadAllBytes(artifactPath));
        ContentManifestEntry font = Assert.Single(result.Manifest.Entries, entry => entry.DeclaredType == "font-json");
        ContentManifestEntry shader = Assert.Single(result.Manifest.Entries, entry => entry.DeclaredType == "shader");
        Assert.Equal(fontBytes, File.ReadAllBytes(Path.Combine(outputRoot, font.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(shaderBytes, File.ReadAllBytes(Path.Combine(outputRoot, shader.ArtifactLocator.Replace('/', Path.DirectorySeparatorChar))));
    }
}
