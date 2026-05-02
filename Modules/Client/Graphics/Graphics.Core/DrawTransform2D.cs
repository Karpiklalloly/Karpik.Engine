using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public struct DrawTransform2D
{
    public Vector2 Position;
    public Vector2 Size;
    public Vector2 Origin;
    public float RotationRadians;
    public DrawSpace Space;

    public DrawTransform2D(Vector2 position, Vector2 size)
    {
        Position = position;
        Size = size;
        Origin = default;
        RotationRadians = 0f;
        Space = DrawSpace.Screen;
    }

    public DrawTransform2D(Vector2 position, Vector2 size, Vector2 origin, float rotationRadians, DrawSpace space = DrawSpace.Screen)
    {
        Position = position;
        Size = size;
        Origin = origin;
        RotationRadians = rotationRadians;
        Space = space;
    }
}
