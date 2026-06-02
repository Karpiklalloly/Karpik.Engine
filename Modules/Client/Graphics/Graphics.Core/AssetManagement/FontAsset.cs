using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Client.Graphics.Core.AssetManagement;

public class FontAsset : Asset
{
    public IFont Font { get; set; } = null!;

    public override Type ValueType => typeof(IFont);

    public override object RawValue
    {
        get => Font;
        set => Font = (IFont)value;
    }

    protected override void OnUnload()
    {
        Font.Dispose();
    }
}
