namespace Karpik.Engine.Shared.Assets;

public class TextAsset : Asset
{
    public string Text { get; set; }

    public override Type ValueType => typeof(string);

    public override object RawValue
    {
        get => Text;
        set => Text = (string)value;
    }

    protected override void OnUnload() => Text = null;
}