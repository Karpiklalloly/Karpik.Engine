using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class Camera
{
    public static Camera Main { get; } = new Camera();
    
    public Camera3D CameraReference => _camera;

    public Vector3 Position
    {
        get => _camera.Position;
        set => _camera.Position = value;
    }
    
    public Vector3 Forward => Raylib.GetCameraForward(ref _camera);
    public Vector3 Up => Raylib.GetCameraUp(ref _camera);
    public Vector3 Right => Raylib.GetCameraRight(ref _camera);
    
    public float FovY
    {
        get => _camera.FovY;
        set => _camera.FovY = value;
    }

    private Camera3D _camera = new Camera3D
    {
        Position = new Vector3(0, 10, 10),
        Target = new Vector3(0, 0, 0),
        Up = new Vector3(0, 1, 0),
        FovY = 45.0f,
        Projection = CameraProjection.Perspective
    };
    
    public void LookAt(Vector3 target)
    {
        _camera.Target = target;
    }

    public void Rotate(Vector2 delta)
    {
        Raylib.CameraYaw(ref _camera, -delta.X, false);
        Raylib.CameraPitch(ref _camera, -delta.Y, true ,false ,true);
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="delta">x - forward, y - right, z - up</param>
    public void Move(Vector3 delta)
    {
        Raylib.CameraMoveForward(ref _camera, delta.X, false);
        Raylib.CameraMoveRight(ref _camera, delta.Y, false);
        Raylib.CameraMoveUp(ref _camera, -delta.Z);
    }
}