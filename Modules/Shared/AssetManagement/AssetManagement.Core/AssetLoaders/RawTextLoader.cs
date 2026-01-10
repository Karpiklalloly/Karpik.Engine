namespace Karpik.Engine.Shared.AssetManagement.Base;

public class RawTextLoader : BaseAssetLoader<TextAsset, string>
{
    public override string? DefaultPath => null;
    public override string[] SupportedExtensions { get; } = [".txt", ".cfg", ".ini", ".log", ".md"];

    protected override async JobHandle<string?> OnLoadAsync(Stream stream, string assetName)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    protected override TextAsset EmptyAsset() => new();

    protected override void SetValue(TextAsset asset, string value) => asset.Text = value;
}