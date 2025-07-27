using System.Numerics;

namespace Karpik.Engine.Shared;

public static class NumericsExtensions
{
    public static Vector2 Normalized(this Vector2 vector)
    {
        var length = vector.Length();
        return new Vector2(vector.X / length, vector.Y / length);
    }
    
    public static void Normalize(ref this Vector2 vector) => vector = Normalized(vector);
}