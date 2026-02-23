namespace Karpik.Engine.Shared.AssetManagement.Core;

public class TextAsset : Asset
{
    public string Text { get; set; }

    public override Type ValueType => typeof(string);

    public override object RawValue
    {
        get => Text;
        set => Text = (string)value;
    }
}