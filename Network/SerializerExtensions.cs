using System.Numerics;
using DCFApixels.DragonECS;
using LiteNetLib.Utils;

namespace Network;

public static class SerializerExtensions
{
    public static void Put(this NetDataWriter writer, Vector2 vector)
    {
        writer.Put(vector.X);
        writer.Put(vector.Y);
    }
    
    public static Vector2 GetVector2(this NetDataReader reader)
    {
        return new Vector2(
            reader.GetFloat(),
            reader.GetFloat()
        );
    }

    public static void Put(this NetDataWriter writer, entlong entity)
    {
        
        writer.Put(entity.ID);
    }

    public static entlong Getentlong(this NetDataReader reader)
    {
        return new entlong(reader.GetInt(), 0, 0);
    }
}