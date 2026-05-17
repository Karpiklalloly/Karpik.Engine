using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
using ImGuiNET;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.ECS;
using Veldrid;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class DisplaySystem : ISystemUpdate, ISystemInit
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<Position> position = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    // [DI] private IRenderer _renderer;
    [DI] private Time _time = null!;
    [DI] private IAssetsManager _assetsManager = null!;
    [DI] private GraphicsCameraState _cameraState = null!;
    [DI] private Input _input = null!;
    [DI] private ImGuiOverlayState _overlay = null!;
    private AssetHandle<TextureAsset> _asset;
    private AssetHandle<FontAsset> _fontAsset;
    private entlong _cameraHolder;

    public void Update()
    {
        var camera = _cameraHolder.Get<CameraHolder>().Camera;
        _cameraState.SetActive(in camera);
        
        foreach (var e in _world.Where(out Aspect a))
        {
            ref readonly var pos = ref a.position.Get(e);
            // _renderer.DrawSphere(new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z),
            //     1,
            //     Color.Red);
        }

        // _cameraHolder.Get<CameraHolder>().Camera = camera;
    }

    public void Init()
    {
        _asset = _assetsManager.LoadAssetAsync<TextureAsset>("Sprites/Player.png").GetAwaiter().GetResult();
        var camera = Camera2D.CreateDefault();
        camera.Position = new Vector2(0, 0);
        camera.Zoom = 1f;
        camera.PixelsPerUnit = 30;
        camera.RotationRadians = 0;
        if (_world.GetPool<CameraHolder>().Count == 0)
        {
            _cameraHolder = _world.NewEntityLong();
            _cameraHolder.Add<CameraHolder>() = new CameraHolder()
            {
                Camera = camera
            };
        }
        else
        {
            _cameraHolder = _world.GetEntityLong(_world.Where(EcsStaticMask.Inc<CameraHolder>().Build()).First());
             _cameraHolder.Get<CameraHolder>().Camera = camera;
        }

        _fontAsset = _assetsManager.LoadAssetAsync<FontAsset>("PressStart.font-json").GetAwaiter().GetResult();
    }
}
