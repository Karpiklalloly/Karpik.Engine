namespace Karpik.Engine.Shared.Assets;

public class TextAsset : Asset
{
    public string Text { get; set; }

    protected override void OnUnload() => Text = null;
}