using System.Numerics;
using DCFApixels.DragonECS;

namespace Karpik.Engine.Client.Graphics.Core;

public struct Camera2DComponent : IEcsComponent
{
    public Vector2 Position;
    public Vector2 TargetPosition;
    public float Zoom;
    public float Rotation;
    public Vector2 ViewportSize;
    public float SmoothingFactor;
}
