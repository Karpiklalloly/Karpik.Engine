using System.Numerics;

namespace Karpik.Engine.Shared.Network.Core;

public static class SerializerExtensions
{
    public static void Put(this IWriter writer, Vector2 vector)
    {
        writer.Put(vector.X);
        writer.Put(vector.Y);
    }
    
    public static Vector2 GetVector2(this IReader reader)
    {
        return new Vector2(
            reader.GetFloat(),
            reader.GetFloat()
        );
    }
    
    public static void Put(this IWriter writer, Vector3 vector)
    {
        writer.Put(vector.X);
        writer.Put(vector.Y);
        writer.Put(vector.Z);
    }
    
    public static Vector3 GetVector3(this IReader reader)
    {
        return new Vector3(
            reader.GetFloat(),
            reader.GetFloat(),
            reader.GetFloat()
        );
    }
}