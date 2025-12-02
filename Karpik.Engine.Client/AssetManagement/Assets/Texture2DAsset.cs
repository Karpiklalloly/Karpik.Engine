using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client.AssetManagement.Assets;

public class Texture2DAsset : Asset
{
    public Texture2D Texture { get; set; }

    public override Type ValueType => typeof(Texture2D);

    public override object RawValue
    {
        get => Texture;
        set => Texture = (Texture2D)value;
    }

    protected override void OnUnload()
    {
        Raylib.UnloadTexture(Texture);
    }
}