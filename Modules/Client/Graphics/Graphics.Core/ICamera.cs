using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public interface ICamera
{
    public Vector3 Position { get; set; }
    public Vector3 Forward { get; }
    public Vector3 Up { get; }
    public Vector3 Right { get; }
    public float FovY { get; set; }
    public void LookAt(Vector3 target);
    public void Rotate(Vector2 delta);
    public void Move(Vector3 delta);
}