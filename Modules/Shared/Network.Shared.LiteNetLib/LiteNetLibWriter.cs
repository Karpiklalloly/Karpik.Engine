using Karpik.Engine.Shared.Network.Core;
using LiteNetLib.Utils;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

public class LiteNetLibWriter : IWriter
{
    public NetDataWriter Writer { get; }
    
    public LiteNetLibWriter(NetDataWriter writer)
    {
        Writer = writer;
    }

    public void Put(float value) => Writer.Put(value);

    public void Put(int value) => Writer.Put(value);

    public void Put(bool value) => Writer.Put(value);

    public void Put(string value) => Writer.Put(value);

    public void Put(byte value) => Writer.Put(value);
    public void Reset()
    {
        Writer.Reset();
    }
}