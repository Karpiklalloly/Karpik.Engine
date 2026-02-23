using Karpik.Engine.Shared.Network.Core;
using LiteNetLib;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

public class LiteNetLibReader : IReader
{
    public NetPacketReader Reader { get; }
    
    public LiteNetLibReader(NetPacketReader reader)
    {
        Reader = reader;
    }
    
    public int AvailableBytes => Reader.AvailableBytes;

    public void Recycle()
    {
        Reader.Recycle();
    }

    public float GetFloat()
    {
        return Reader.GetFloat();
    }

    public byte GetByte()
    {
        return Reader.GetByte();
    }

    public ushort GetUShort()
    {
        return Reader.GetUShort();
    }

    public int GetInt()
    {
        return Reader.GetInt();
    }

    public long GetLong()
    {
        return Reader.GetLong();
    }

    public double GetDouble()
    {
        return Reader.GetDouble();
    }

    public bool GetBool()
    {
        return Reader.GetBool();
    }

    public string GetString()
    {
        return Reader.GetString();
    }
}