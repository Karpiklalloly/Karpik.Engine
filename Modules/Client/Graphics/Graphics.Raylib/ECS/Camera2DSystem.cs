using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Client.Graphics.GRaylib;

public class Camera2DSystem : IEcsRun
{
    class Aspect : EcsAspect
    {
        public EcsPool<Camera2DComponent> Camera = Inc;
        public EcsPool<ActiveCamera2DTag> Active = Opt;
    }
    
    [DI] private EcsDefaultWorld _world = null!;
    [DI] private ICamera2D _mainCamera = null!;
    
    public void Run()
    {
        var entities = _world.Where(out Aspect a);
        
        foreach (var e in entities)
        {
            ref var cam = ref a.Camera.Get(e);
            cam.Position = Vector2.Lerp(cam.Position, cam.TargetPosition, cam.SmoothingFactor);
            
            if (a.Active.Has(e))
            {
                _mainCamera.Position = cam.Position;
                _mainCamera.Zoom = cam.Zoom;
                _mainCamera.Rotation = cam.Rotation;
                _mainCamera.ViewportSize = cam.ViewportSize;
            }
        }
    }
}
