using SSSuperGame.Shared.CoinRush;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks deterministic movement, timer, scoring, and interpolation rules.</summary>
public sealed class MatchRulesTests
{
    /// <summary>Verifies an immediate jump from a grounded button press.</summary>
    [Fact]
    public void Grounded_press_jumps_immediately()
    {
        var mv = new MoveState();
        bool jump = MatchRules.UpdateJump(ref mv, grounded: true, jumpPressed: true, dt: 1f / 60f);
        Assert.True(jump);
        Assert.Equal(0f, mv.BufferTimer);
    }

    /// <summary>Verifies a jump press remains buffered until landing.</summary>
    [Fact]
    public void Jump_is_buffered_before_landing()
    {
        var mv = new MoveState();
        bool airJump = MatchRules.UpdateJump(ref mv, grounded: false, jumpPressed: true, dt: 1f / 60f);
        Assert.False(airJump);
        Assert.True(mv.BufferTimer > 0f);
        bool landJump = MatchRules.UpdateJump(ref mv, grounded: true, jumpPressed: false, dt: 1f / 60f);
        Assert.True(landJump);
    }

    /// <summary>Verifies an expired buffered jump does not fire.</summary>
    [Fact]
    public void Stale_buffer_does_not_fire()
    {
        var mv = new MoveState();
        MatchRules.UpdateJump(ref mv, grounded: false, jumpPressed: true, dt: 1f / 60f);
        for (int i = 0; i < 30; i++)
        {
            MatchRules.UpdateJump(ref mv, grounded: false, jumpPressed: false, dt: 1f / 60f);
        }
        Assert.Equal(0f, mv.BufferTimer);
        bool late = MatchRules.UpdateJump(ref mv, grounded: true, jumpPressed: false, dt: 1f / 60f);
        Assert.False(late);
    }

    /// <summary>Verifies a jump shortly after leaving the ground.</summary>
    [Fact]
    public void Coyote_time_allows_late_jump()
    {
        var mv = new MoveState();
        MatchRules.UpdateJump(ref mv, grounded: true, jumpPressed: false, dt: 1f / 60f);
        Assert.True(mv.CoyoteTimer > 0f);
        bool jump = MatchRules.UpdateJump(ref mv, grounded: false, jumpPressed: true, dt: 1f / 60f);
        Assert.True(jump);
    }

    /// <summary>Verifies a jump is denied after coyote time expires.</summary>
    [Fact]
    public void Expired_coyote_denies_jump()
    {
        var mv = new MoveState();
        MatchRules.UpdateJump(ref mv, grounded: true, jumpPressed: false, dt: 1f / 60f);
        for (int i = 0; i < 30; i++)
        {
            MatchRules.UpdateJump(ref mv, grounded: false, jumpPressed: false, dt: 1f / 60f);
        }
        bool jump = MatchRules.UpdateJump(ref mv, grounded: false, jumpPressed: true, dt: 1f / 60f);
        Assert.False(jump);
    }

    /// <summary>Verifies jump velocity reaches the configured jump height.</summary>
    [Fact]
    public void Jump_velocity_reaches_jump_height()
    {
        float v = MatchRules.JumpVelocity();
        float h = (v * v) / (2f * -MatchRules.Gravity);
        Assert.Equal(MatchRules.JumpHeight, h, precision: 3);
    }

    /// <summary>Verifies winner selection and tied scores.</summary>
    [Fact]
    public void Winner_and_draw()
    {
        Assert.Equal(0, MatchRules.Winner(5, 3));
        Assert.Equal(1, MatchRules.Winner(2, 7));
        Assert.Equal(MatchRules.Draw, MatchRules.Winner(4, 4));
        Assert.Equal(-1, MatchRules.Winner(0, 0));
    }

    /// <summary>Verifies timer decrement and match completion.</summary>
    [Fact]
    public void Timer_ticks_and_ends_match()
    {
        float t = MatchRules.TickTimer(90f, 1f / 60f);
        Assert.True(t < 90f && t > 89f);
        Assert.False(MatchRules.IsMatchOver(t));
        Assert.True(MatchRules.IsMatchOver(0f));
        Assert.Equal(0f, MatchRules.TickTimer(0.001f, 1f));
    }

    /// <summary>Verifies the coin respawn timer and ready state.</summary>
    [Fact]
    public void Coin_respawn_cycle()
    {
        float t = MatchRules.CoinRespawnDelay;
        Assert.False(MatchRules.CoinReady(t));
        t = MatchRules.TickCoin(t, t);
        Assert.True(MatchRules.CoinReady(t));
        Assert.Equal(0f, MatchRules.TickCoin(0f, 1f));
    }

    /// <summary>Verifies that players below the kill plane are detected.</summary>
    [Fact]
    public void Fall_out_detection()
    {
        Assert.True(MatchRules.FellOut(LevelData.KillY - 1f, LevelData.KillY));
        Assert.False(MatchRules.FellOut(0f, LevelData.KillY));
    }

    /// <summary>Verifies a moving platform reverses at the endpoint.</summary>
    [Fact]
    public void Mover_ping_pongs()
    {
        var m = new MovingPlatform { FromX = 0f, FromY = 0f, ToX = 10f, ToY = 0f, Speed = 10f, Phase = 0f, Direction = 1 };
        for (int i = 0; i < 61; i++)
        {
            MatchRules.StepMover(ref m, 1f / 60f);
        }
        Assert.Equal(1f, m.Phase, precision: 4);
        Assert.Equal(-1, m.Direction);
        MatchRules.MoverPosition(in m, out float x, out float y);
        Assert.Equal(10f, x);
        Assert.Equal(0f, y);
    }

    /// <summary>Verifies fallback spawn and coin data have the expected sizes.</summary>
    [Fact]
    public void Respawn_anchors_default_to_player_spawns()
    {
        Assert.Equal(2, LevelData.PlayerSpawns.Length);
        Assert.Equal(12, LevelData.Coins.Length);
        Assert.Equal(LevelData.CoinCount, LevelData.Coins.Length);
    }

    /// <summary>Verifies interpolation clamps values outside the unit interval.</summary>
    [Fact]
    public void Lerp_clamps()
    {
        Assert.Equal(0f, MatchRules.Lerp(0f, 10f, -1f));
        Assert.Equal(10f, MatchRules.Lerp(0f, 10f, 2f));
        Assert.Equal(5f, MatchRules.Lerp(0f, 10f, 0.5f));
    }

    /// <summary>Verifies snapshot interpolation progress over the arrival interval.</summary>
    [Fact]
    public void Snapshot_progress_spreads_a_snapshot_over_its_arrival_interval()
    {
        Assert.Equal(0f, MatchRules.SnapshotProgress(0f, 0.05f));
        Assert.Equal(0.5f, MatchRules.SnapshotProgress(0.025f, 0.05f));
        Assert.Equal(1f, MatchRules.SnapshotProgress(0.1f, 0.05f));
    }

    /// <summary>Verifies snapshot spacing uses authoritative tick differences.</summary>
    [Fact]
    public void Snapshot_interval_uses_authoritative_tick_spacing()
    {
        Assert.Equal(0.05f, MatchRules.SnapshotInterval(3, 1f / 60f), precision: 4);
        Assert.Equal(0.1f, MatchRules.SnapshotInterval(6, 1f / 60f), precision: 4);
    }

    /// <summary>Verifies input latching preserves a jump edge across packets.</summary>
    [Fact]
    public void Latch_accumulates_jump_across_packets()
    {
        var latch = new InputLatch { Sequence = -1 };
        Assert.True(MatchRules.AccumulateLatch(ref latch, new InputCommand { Sequence = 1, MoveAxis = 1f, Jump = true }));
        Assert.True(MatchRules.AccumulateLatch(ref latch, new InputCommand { Sequence = 2, MoveAxis = 1f, Jump = false }));
        Assert.Equal(1, latch.Jump);
        Assert.Equal(1f, latch.MoveAxis);
        Assert.Equal(2, latch.Sequence);
    }

    /// <summary>Verifies input latching rejects stale sequence numbers.</summary>
    [Fact]
    public void Latch_ignores_stale_sequences()
    {
        var latch = new InputLatch { Sequence = -1 };
        Assert.True(MatchRules.AccumulateLatch(ref latch, new InputCommand { Sequence = 5, MoveAxis = -1f, Jump = true }));
        Assert.False(MatchRules.AccumulateLatch(ref latch, new InputCommand { Sequence = 5, MoveAxis = 0f, Jump = false }));
        Assert.False(MatchRules.AccumulateLatch(ref latch, new InputCommand { Sequence = 3, MoveAxis = 0f, Jump = false }));
        Assert.Equal(1, latch.Jump);
        Assert.Equal(-1f, latch.MoveAxis);
        Assert.Equal(5, latch.Sequence);
    }
}
