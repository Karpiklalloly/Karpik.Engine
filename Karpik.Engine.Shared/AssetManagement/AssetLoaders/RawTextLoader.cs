using Karpik.Engine.Shared.Assets;

namespace Karpik.Engine.Shared;

public class RawTextLoader : BaseAssetLoader<TextAsset>
{
    public override string[] SupportedExtensions { get; } = [".txt", ".cfg", ".ini", ".log", ".md"];

    protected override async Task<TextAsset> OnLoadAsync(Stream stream, string assetName)
    {
        using StreamReader reader = new StreamReader(stream);
        string content = await reader.ReadToEndAsync();
        return new TextAsset { Text = content };
    }
}