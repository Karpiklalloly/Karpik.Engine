using SSSuperGame.Shared.CoinRush;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks packet encoding, decoding, and delivery contracts.</summary>
public sealed class NetPayloadTests
{
    /// <summary>Verifies an input command round trip through the network codec.</summary>
    [Fact]
    public void Input_command_round_trip()
    {
        var src = new InputCommand { Sequence = 513, MoveAxis = -0.5f, Jump = true, RestartRequest = false };
        var w = new FakeWriter();
        src.Write(w);
        var r = new FakeReader(w.Values);
        Assert.True(InputCommand.TryRead(r, out InputCommand dst));
        Assert.Equal(src.Sequence, dst.Sequence);
        Assert.Equal(src.MoveAxis, dst.MoveAxis);
        Assert.Equal(src.Jump, dst.Jump);
        Assert.Equal(src.RestartRequest, dst.RestartRequest);
        Assert.Equal(0, r.AvailableBytes);
    }

    /// <summary>Verifies a network codec round trip for every match event kind.</summary>
    [Fact]
    public void Match_event_round_trip_all_kinds()
    {
        foreach (MatchEventKind kind in new[] { MatchEventKind.MatchStart, MatchEventKind.CoinPickup, MatchEventKind.Respawn, MatchEventKind.MatchFinish, MatchEventKind.FullState })
        {
            var src = new MatchEvent { Kind = kind, PlayerIndex = 1, IntArg = 7, FloatArg = 0.5f };
            var w = new FakeWriter();
            src.Write(w);
            var r = new FakeReader(w.Values);
            Assert.True(MatchEvent.TryRead(r, out MatchEvent dst));
            Assert.Equal(src.Kind, dst.Kind);
            Assert.Equal(src.PlayerIndex, dst.PlayerIndex);
            Assert.Equal(src.IntArg, dst.IntArg);
            Assert.Equal(src.FloatArg, dst.FloatArg);
        }
    }

    /// <summary>Verifies packet decoding rejects a different packet kind.</summary>
    [Fact]
    public void Wrong_kind_is_rejected()
    {
        var w = new FakeWriter();
        new MatchEvent { Kind = MatchEventKind.MatchStart }.Write(w);
        Assert.False(InputCommand.TryRead(new FakeReader(w.Values), out _));

        var w2 = new FakeWriter();
        new InputCommand { Sequence = 1 }.Write(w2);
        Assert.False(MatchEvent.TryRead(new FakeReader(w2.Values), out _));
    }

    /// <summary>Verifies delivery methods for frequent input and reliable events.</summary>
    [Fact]
    public void Delivery_methods_match_design()
    {
        Assert.Equal(Karpik.Engine.Shared.Network.Core.DeliveryMethod.Sequenced, InputCommand.Delivery);
        Assert.Equal(Karpik.Engine.Shared.Network.Core.DeliveryMethod.ReliableOrdered, MatchEvent.Delivery);
    }

    /// <summary>Verifies game packet kinds stay outside the engine envelope range.</summary>
    [Fact]
    public void Packet_kinds_avoid_engine_envelope_range()
    {
        Assert.NotEqual(PacketKind.Input, PacketKind.Snapshot);
        Assert.NotEqual(PacketKind.Snapshot, PacketKind.Event);
        Assert.True(PacketKind.Input > 2 && PacketKind.Snapshot > 2 && PacketKind.Event > 2);
    }
}
