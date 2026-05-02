using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Client.Graphics.Core.AssetManagement;

public class TextureAsset : Asset
{
    public ITexture2D Texture { get; set; }
    
    public override Type ValueType => typeof(ITexture2D);

    public override object RawValue
    {
        get => Texture;
        set => Texture = (ITexture2D)value;
    }

    protected override void OnUnload()
    {
        Texture.Dispose();
    }
}
