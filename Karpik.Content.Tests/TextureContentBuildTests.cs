using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class TextureContentBuildTests
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL4uQAAAABJRU5ErkJggg==");

    [Fact]
    public void Build_MixedJsonAndTextureSources_PublishesTextureArtifact()
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
    }
}
