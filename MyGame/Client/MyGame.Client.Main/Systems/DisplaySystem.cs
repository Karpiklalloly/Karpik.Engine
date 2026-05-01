using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class DisplaySystem : IEcsRun, IEcsInit
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<Position> position = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    // [DI] private IRenderer _renderer;
    [DI] private Drawer _drawer;
    [DI] private Time _time = null!;
    [DI] private IAssetsManager _assetsManager = null!;
    private AssetHandle<TextureAsset> _asset;

    public void Run()
    {
        GraphicsContext.Buffer.Add(new DrawRectCmd()
        {
            Color = Color.White,
            Rectangle = new RectangleF(0, 0, 100 * (MathF.Sin((float)_time.TotalTime) + 1) + 20f, 100)
        });
        
        GraphicsContext.Buffer.Add(new DrawRectCmd()
        {
            Color = Color.Blue,
            Rectangle = new RectangleF(0, 100, 100 * (MathF.Sin((float)_time.TotalTime) + 1) + 20f, 100)
        });
        
        GraphicsContext.Buffer.Add(new DrawRectCmd()
        {
            Color = Color.Red,
            Rectangle = new RectangleF(0, 200, 100 * (MathF.Sin((float)_time.TotalTime) + 1) + 20f, 100)
        });
        
        GraphicsContext.Buffer.Add(new DrawTextureCmd()
        {
            Color = Color.White,
            Position = new Vector2(0, 300),
            Size = new Vector2(100 * (MathF.Sin((float)_time.TotalTime) + 1) + 20f, 100),
            Texture = _asset.Asset!.Texture
        });
        foreach (var e in _world.Where(out Aspect a))
        {
            ref readonly var pos = ref a.position.Get(e);
            // _renderer.DrawSphere(new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z),
            //     1,
            //     Color.Red);
        }
    }

    public void Init()
    {
        _asset = _assetsManager.LoadAssetAsync<TextureAsset>("Sprites/Player.png").GetAwaiter().GetResult();
    }
}