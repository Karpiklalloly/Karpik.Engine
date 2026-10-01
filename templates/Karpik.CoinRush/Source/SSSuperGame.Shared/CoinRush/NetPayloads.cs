using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.DragonECS;

namespace SSSuperGame.Shared.CoinRush;

/// <summary>Identifies custom game packet payloads on the network transport.</summary>
public static class PacketKind
{
    public const byte Input = 41;
    public const byte Snapshot = 42;
    public const byte Event = 43;
}

/// <summary>Defines the transport channel used for game traffic.</summary>
public static class NetChannel
{
    public const byte Gameplay = 0;
}

/// <summary>Contains one sequenced player input command.</summary>
public struct InputCommand
{
    public int Sequence;
    public float MoveAxis;
    public bool Jump;
    public bool RestartRequest;

    public static readonly DeliveryMethod Delivery = DeliveryMethod.Sequenced;

    /// <summary>Writes this command, including its packet discriminator.</summary>
    /// <param name="w">The packet writer.</param>
    public void Write(IWriter w)
    {
        w.Put(PacketKind.Input);
        w.Put(Sequence);
        w.Put(MoveAxis);
        w.Put(Jump);
        w.Put(RestartRequest);
    }

    /// <summary>Attempts to read an input command including its packet discriminator.</summary>
    /// <param name="r">The packet reader.</param>
    /// <param name="cmd">Receives the decoded command on success.</param>
    /// <returns>Whether the packet is a complete input command.</returns>
    public static bool TryRead(IReader r, out InputCommand cmd)
    {
        cmd = default;
        if (r.AvailableBytes < 1)
        {
            return false;
        }
        if (r.GetByte() != PacketKind.Input)
        {
            return false;
        }
        return TryReadBody(r, out cmd);
    }

    /// <summary>Attempts to read an input command after its packet discriminator was consumed.</summary>
    /// <param name="r">The reader positioned at the command body.</param>
    /// <param name="cmd">Receives the decoded command on success.</param>
    /// <returns>Whether the command body was complete.</returns>
    public static bool TryReadBody(IReader r, out InputCommand cmd)
    {
        cmd = default;
        try
        {
            cmd.Sequence = r.GetInt();
            cmd.MoveAxis = r.GetFloat();
            cmd.Jump = r.GetBool();
            cmd.RestartRequest = r.GetBool();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Identifies reliable game events sent between server and clients.</summary>
public enum MatchEventKind : byte
{
    MatchStart = 1,
    CoinPickup = 2,
    Respawn = 3,
    MatchFinish = 4,
    FullState = 5,
}

/// <summary>Contains a reliable match event and its small payload.</summary>
public struct MatchEvent : IEcsComponentEvent
{
    /// <summary>The source entity identifier.</summary>
    public int Source { get; set; }
    /// <summary>The target entity identifier.</summary>
    public int Target { get; set; }
    public MatchEventKind Kind;
    public int PlayerIndex;
    public int IntArg;
    public float FloatArg;

    public static readonly DeliveryMethod Delivery = DeliveryMethod.ReliableOrdered;

    /// <summary>Writes this event, including its packet discriminator.</summary>
    /// <param name="w">The packet writer.</param>
    public void Write(IWriter w)
    {
        w.Put(PacketKind.Event);
        w.Put((byte)Kind);
        w.Put(PlayerIndex);
        w.Put(IntArg);
        w.Put(FloatArg);
    }

    /// <summary>Attempts to read a match event including its packet discriminator.</summary>
    /// <param name="r">The packet reader.</param>
    /// <param name="ev">Receives the decoded event on success.</param>
    /// <returns>Whether the packet is a complete match event.</returns>
    public static bool TryRead(IReader r, out MatchEvent ev)
    {
        ev = default;
        if (r.AvailableBytes < 1)
        {
            return false;
        }
        if (r.GetByte() != PacketKind.Event)
        {
            return false;
        }
        return TryReadBody(r, out ev);
    }

    /// <summary>Attempts to read a match event after its packet discriminator was consumed.</summary>
    /// <param name="r">The reader positioned at the event body.</param>
    /// <param name="ev">Receives the decoded event on success.</param>
    /// <returns>Whether the event body was complete.</returns>
    public static bool TryReadBody(IReader r, out MatchEvent ev)
    {
        ev = default;
        try
        {
            ev.Kind = (MatchEventKind)r.GetByte();
            ev.PlayerIndex = r.GetInt();
            ev.IntArg = r.GetInt();
            ev.FloatArg = r.GetFloat();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
