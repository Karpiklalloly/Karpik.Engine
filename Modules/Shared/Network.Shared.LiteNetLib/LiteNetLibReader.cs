using System.Drawing;
using Karpik.Engine.Shared.Network.Core;
using LiteNetLib;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

public sealed class LiteNetLibReader : IReader
{
    private NetPacketReader? _reader;
    private readonly bool _borrowed;

    private NetPacketReader Reader => _reader ?? throw new InvalidOperationException("The reader is not in a receive callback.");
    public long GameplayRevision { get; private set; }

    public LiteNetLibReader(NetPacketReader reader) => _reader = reader;
    internal LiteNetLibReader() => _borrowed = true;

    internal void Bind(NetPacketReader? reader, long revision)
    {
        _reader = reader;
        GameplayRevision = revision;
    }

    public int AvailableBytes => Reader.AvailableBytes;
    public void Recycle() { if (!_borrowed) Reader.Recycle(); }
    public float GetFloat() => Reader.GetFloat();
    public byte GetByte() => Reader.GetByte();
    public ushort GetUShort() => Reader.GetUShort();
    public int GetInt() => Reader.GetInt();
    public long GetLong() => Reader.GetLong();
    public double GetDouble() => Reader.GetDouble();
    public bool GetBool() => Reader.GetBool();
    public string GetString() => Reader.GetString();
    public Color GetColor() => Color.FromArgb(Reader.GetInt());
}
