using System.Drawing;
using Karpik.Engine.Shared.Network.Core;
using LiteNetLib.Utils;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

public class LiteNetLibWriter : IWriter
{
    internal NetDataWriter Writer { get; }
    private readonly int _payloadOffset;

    public LiteNetLibWriter(NetDataWriter writer) : this(writer, reserveGameplayHeader: false) { }

    internal LiteNetLibWriter(NetDataWriter writer, bool reserveGameplayHeader)
    {
        Writer = writer;
        if (reserveGameplayHeader)
        {
            // Writers can be created before opt-in. Legacy sends slice off this unused space.
            _payloadOffset = LiteNetLibNetworkManager.GameplayHeaderBytes;
            Reset();
        }
    }

    internal ReadOnlySpan<byte> GameplayPayload => Writer.AsReadOnlySpan()[_payloadOffset..];

    internal ReadOnlySpan<byte> GetRevisionFrame(long revision)
    {
        if (_payloadOffset == 0)
            throw new ArgumentException("Use the network manager's CreateWriter for revision gameplay.");
        LiteNetLibNetworkManager.WriteGameplayHeader(Writer.Data, revision);
        return Writer.AsReadOnlySpan();
    }

    public void Put(float value) => Writer.Put(value);

    public void Put(int value) => Writer.Put(value);

    public void Put(long value) => Writer.Put(value);

    public void Put(bool value) => Writer.Put(value);

    public void Put(string value) => Writer.Put(value);

    public void Put(byte value) => Writer.Put(value);
    public void Put(ushort value) => Writer.Put(value);
    public void Put(double value) => Writer.Put(value);
    public void Put(Color color) => Writer.Put(color.ToArgb());

    public void Reset() => Writer.SetPosition(_payloadOffset);
}
