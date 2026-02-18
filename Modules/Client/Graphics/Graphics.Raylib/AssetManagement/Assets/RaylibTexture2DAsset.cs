using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Raylib_cs;

namespace Karpik.Engine.Client.Graphics.GRaylib;

public class RaylibTexture2DAsset : Texture2DAsset
{
    public override ITexture2D Texture => TextureRaylib;
    public required RaylibTexture2D TextureRaylib { get; set; }

    public override Type ValueType => typeof(Texture2D);
    
    public float Width => TextureRaylib.Width;
    public float Height => TextureRaylib.Height;

    public override object RawValue
    {
        get => TextureRaylib;
        set => TextureRaylib = (RaylibTexture2D)value;
    }

    protected override void OnUnload()
    {
        
        // Check if Raylib context is still valid before unloading
        // IsWindowReady returns false if the window/context was destroyed
        Console.WriteLine(Environment.CurrentManagedThreadId);
        if (Raylib.IsWindowReady() && TextureRaylib.Texture.Id != 0)
        {
            Raylib.UnloadTexture(TextureRaylib.Texture);
        }
    }
}