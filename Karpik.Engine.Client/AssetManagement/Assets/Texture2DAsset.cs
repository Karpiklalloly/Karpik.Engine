using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client.AssetManagement.Assets;

public class Texture2DAsset : Asset
{
    public Texture2D Texture { get; }
    
    public Texture2DAsset(Texture2D texture)
    {
        Texture = texture;
    }

    protected override void OnUnload()
    {
        Raylib.UnloadTexture(Texture);
    }
}