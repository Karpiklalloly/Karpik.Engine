using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;

namespace Karpik.Engine.Client.Graphics.Core.AssetManagement;

public class FontLoader : BaseAssetLoader<FontAsset, FontLoader.LoadedFont>
{
    public override string? DefaultPath => null;
    public override string[] SupportedExtensions => [".font-json"];

    protected override async JobHandle<LoadedFont> OnLoadAsync(Stream stream, string assetName)
    {
        using MemoryStream memory = new MemoryStream();
        await stream.CopyToAsync(memory);

        FontAtlasData fontData = FontAtlasParser.Parse(memory.GetBuffer().AsSpan(0, (int)memory.Length));
        string atlasPath = ResolveAtlasPath(assetName, fontData.AtlasPath);
        AssetHandle<TextureAsset> textureHandle = await AssetsManager.LoadAssetAsync<TextureAsset>(atlasPath);

        TextureAsset? textureAsset = textureHandle.Asset;
        if (textureAsset is null)
        {
            textureHandle.Dispose();
            return new LoadedFont();
        }

        AtlasFont font = new AtlasFont(textureAsset.Texture, in fontData.Metrics, fontData.Glyphs, ownsAtlasTexture: false);
        return new LoadedFont(font, textureAsset, textureHandle);
    }

    protected override FontAsset EmptyAsset() => new();

    protected override void SetValue(FontAsset asset, LoadedFont value)
    {
        asset.Font = value.Font;
        asset.AddDependency(value.TextureAsset);
        value.TextureHandle.Dispose();
    }

    private static string ResolveAtlasPath(string assetName, string atlasPath)
    {
        if (string.IsNullOrEmpty(atlasPath))
        {
            return Path.ChangeExtension(assetName, ".png");
        }

        if (Path.IsPathRooted(atlasPath))
        {
            return atlasPath;
        }

        string? directory = Path.GetDirectoryName(assetName);
        return string.IsNullOrEmpty(directory)
            ? atlasPath
            : Path.Combine(directory, atlasPath);
    }

    public readonly struct LoadedFont
    {
        public readonly AtlasFont Font;
        public readonly TextureAsset TextureAsset;
        public readonly AssetHandle<TextureAsset> TextureHandle;

        public LoadedFont(AtlasFont font, TextureAsset textureAsset, AssetHandle<TextureAsset> textureHandle)
        {
            Font = font;
            TextureAsset = textureAsset;
            TextureHandle = textureHandle;
        }
    }
}
