using System.Drawing;
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
    public void Put(ushort value) => Writer.Put(value);
    public void Put(double value) => Writer.Put(value);
    public void Put(Color color) => Writer.Put(color.ToArgb());

    public void Reset() => Writer.Reset();
}