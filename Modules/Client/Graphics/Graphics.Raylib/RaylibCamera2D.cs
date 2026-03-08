using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Raylib_cs;

namespace Karpik.Engine.Client.Graphics.GRaylib;

public class RaylibCamera2D : ICamera2D
{
    private Camera2D _camera;
    
    public RaylibCamera2D()
    {
        // Use default reasonable values - offset will be updated when screen is available
        _camera = new Camera2D
        {
            Offset = new Vector2(640, 360),  // Default half of 1280x720
            Target = new Vector2(0, 0),
            Rotation = 0f,
            Zoom = 1f
        };
    }
    
    public Vector2 Position
    {
        get => _camera.Target;
        set
        {
            _camera.Target = value;
            UpdateOffset();
        }
    }
    
    public float Zoom
    {
        get => _camera.Zoom;
        set => _camera.Zoom = value;
    }
    
    public float Rotation
    {
        get => _camera.Rotation;
        set => _camera.Rotation = value;
    }
    
    public Vector2 ViewportSize
    {
        get => _camera.Offset;
        set
        {
            _camera.Offset = new Vector2(value.X / 2, value.Y / 2);
        }
    }
    
    public Vector2 WorldToScreen(Vector2 worldPosition)
    {
        // Raylib's GetWorldToScreen2D handles the conversion
        return Raylib.GetWorldToScreen2D(worldPosition, _camera);
    }
    
    public Vector2 ScreenToWorld(Vector2 screenPosition)
    {
        return Raylib.GetScreenToWorld2D(screenPosition, _camera);
    }
    
    public Camera2D Raylib2D => _camera;
    
    private void UpdateOffset()
    {
        // Offset is typically set to viewport center
        var screenSize = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        _camera.Offset = new Vector2(screenSize.X / 2, screenSize.Y / 2);
    }
}
