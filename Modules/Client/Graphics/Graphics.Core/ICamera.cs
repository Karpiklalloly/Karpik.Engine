using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public interface ICamera
{
    Vector3 Position { get; set; }
    Vector3 Forward { get; }
    Vector3 Up { get; }
    Vector3 Right { get; }
    float FovY { get; set; }
    void LookAt(Vector3 target);
    void Rotate(Vector2 delta);
    void Move(Vector3 delta);
}