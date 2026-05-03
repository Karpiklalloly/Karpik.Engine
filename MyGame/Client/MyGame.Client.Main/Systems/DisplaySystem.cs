using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.ECS;
using Veldrid;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class DisplaySystem : IEcsRun, IEcsInit
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<Position> position = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    // [DI] private IRenderer _renderer;
    [DI] private Drawer _drawer = null!;
    [DI] private Time _time = null!;
    [DI] private IAssetsManager _assetsManager = null!;
    [DI] private GraphicsCameraState _cameraState = null!;
    [DI] private Input _input = null!;
    private AssetHandle<TextureAsset> _asset;
    private Camera2D _camera;
    private AssetHandle<FontAsset> _fontAsset;

    public void Run()
    {
        float speed = 1;
        if (_input.IsDown(Key.W))
        {
            _camera.Position += new Vector2(0, -100) * (float)_time.DeltaTime * speed;
        }
        if (_input.IsDown(Key.S))
        {
            _camera.Position += new Vector2(0, 100) * (float)_time.DeltaTime * speed;
        }
        if (_input.IsDown(Key.A))
        {
            _camera.Position += new Vector2(-100, 0) * (float)_time.DeltaTime * speed;
        }
        if (_input.IsDown(Key.D))
        {
            _camera.Position += new Vector2(100, 0) * (float)_time.DeltaTime * speed;
        }

        _cameraState.SetActive(in _camera);
        
        var size = new Vector2(100 * (MathF.Sin((float)_time.TotalTime) + 1) + 20f, 100);
        
        GraphicsContext.Buffer.Add(new DrawRectCmd()
        {
            Color = Color.Blue,
            Rectangle = new RectangleF(0, 100, size.X, size.Y)
        });
        
        // Не в центре оригин
        GraphicsContext.Buffer.AddTextCentered(
            _fontAsset.Asset!.Font,
            "Hello, World!",
            new Vector2(0, 0),
            1f + 1 * MathF.Sin((float)_time.TotalTime * 2),
            Color.White,
            rotationRadians: (float)_time.TotalTime,
            space: DrawSpace.World);

        
        GraphicsContext.Buffer.AddTextureCentered(
            _asset.Asset!.Texture,
            new Vector2(120, 300),
            size,
            Color.White,
            (float)_time.TotalTime,
            DrawSpace.Screen);
        
        GraphicsContext.Buffer.AddTextureCentered(
            _asset.Asset!.Texture,
            new Vector2(0, 2),
            new Vector2(1, 1),
            Color.White,
            0,
            DrawSpace.World);
        
        GraphicsContext.Buffer.AddTextureCentered(
            _asset.Asset!.Texture,
            new Vector2(0, 0),
            new Vector2(1, 1),
            Color.White,
            0,
            DrawSpace.World);
        
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
        _camera = Camera2D.CreateDefault();
        _camera.Position = new Vector2(0, 0);
        _camera.Zoom = 1f;
        _camera.PixelsPerUnit = 64;
        _camera.RotationRadians = 0;

        _fontAsset = _assetsManager.LoadAssetAsync<FontAsset>("PressStart.font-json").GetAwaiter().GetResult();

        IFont font = _fontAsset.Asset!.Font;

        Console.WriteLine(font.Size);
        Console.WriteLine(font.LineHeight);
        Console.WriteLine(font.DistanceRange);
        Console.WriteLine(font.TryGetGlyph((uint)'A', out var glyph));
        Console.WriteLine(glyph.SourceUv);
    }
}
