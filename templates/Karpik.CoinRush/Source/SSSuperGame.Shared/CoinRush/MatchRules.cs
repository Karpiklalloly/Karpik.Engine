namespace SSSuperGame.Shared.CoinRush;

/// <summary>Provides deterministic movement, timing, and scoring rules shared by game systems.</summary>
public static class MatchRules
{
    public const float CoyoteTime = 0.12f;
    public const float JumpBufferTime = 0.15f;
    public const float Gravity = -45f;
    public const float MaxFallSpeed = -26f;
    public const float JumpHeight = 1f;
    public const float MoveSpeed = 8f;
    public const float AirControl = 0.75f;
    public const float GroundRayLength = 0.25f;
    public const float CoinRespawnDelay = 5f;
    public const float MatchDuration = 90f;
    public const float CountdownDuration = 3f;
    public const int MaxPlayers = 2;
    public const int Draw = -1;

    /// <summary>Advances jump buffering and coyote time for one fixed tick.</summary>
    /// <param name="mv">The player movement state to update.</param>
    /// <param name="grounded">Whether the player currently touches ground.</param>
    /// <param name="jumpPressed">Whether a jump press arrived this tick.</param>
    /// <param name="dt">The fixed tick duration in seconds.</param>
    /// <returns>Whether a jump should start on this tick.</returns>
    public static bool UpdateJump(ref MoveState mv, bool grounded, bool jumpPressed, float dt)
    {
        mv.Grounded = grounded ? (byte)1 : (byte)0;
        if (grounded)
        {
            mv.CoyoteTimer = CoyoteTime;
        }
        else if (mv.CoyoteTimer > 0f)
        {
            mv.CoyoteTimer -= dt;
            if (mv.CoyoteTimer < 0f)
            {
                mv.CoyoteTimer = 0f;
            }
        }

        if (jumpPressed)
        {
            mv.BufferTimer = JumpBufferTime;
        }
        else if (mv.BufferTimer > 0f)
        {
            mv.BufferTimer -= dt;
            if (mv.BufferTimer < 0f)
            {
                mv.BufferTimer = 0f;
            }
        }

        if (mv.BufferTimer > 0f && (grounded || mv.CoyoteTimer > 0f))
        {
            mv.BufferTimer = 0f;
            mv.CoyoteTimer = 0f;
            return true;
        }
        return false;
    }

    /// <summary>Stores the latest movement axis and accumulates jump and restart presses.</summary>
    /// <param name="latch">The pending input state to update.</param>
    /// <param name="cmd">The received input command.</param>
    /// <returns>Whether the command was newer than the stored sequence.</returns>
    public static bool AccumulateLatch(ref InputLatch latch, InputCommand cmd)
    {
        if (cmd.Sequence <= latch.Sequence)
        {
            return false;
        }
        latch.Sequence = cmd.Sequence;
        latch.MoveAxis = ClampAxis(cmd.MoveAxis);
        if (cmd.Jump)
        {
            latch.Jump = 1;
        }
        if (cmd.RestartRequest)
        {
            latch.Restart = 1;
        }
        latch.HasInput = 1;
        return true;
    }

    /// <summary>Calculates the initial upward speed needed to reach the configured jump height.</summary>
    /// <returns>The initial vertical jump speed.</returns>
    public static float JumpVelocity()
    {
        float g = -Gravity;
        return MathF.Sqrt(2f * g * JumpHeight);
    }

    /// <summary>Restricts a horizontal input axis to the interval from -1 to 1.</summary>
    /// <param name="v">The requested axis value.</param>
    /// <returns>The clamped axis value.</returns>
    public static float ClampAxis(float v)
    {
        if (v < -1f)
        {
            return -1f;
        }
        if (v > 1f)
        {
            return 1f;
        }
        return v;
    }

    /// <summary>Calculates horizontal target speed from input and ground contact.</summary>
    /// <param name="axis">The horizontal input axis.</param>
    /// <param name="grounded">Whether the player is grounded.</param>
    /// <returns>The target horizontal speed.</returns>
    public static float TargetSpeed(float axis, bool grounded)
    {
        return ClampAxis(axis) * MoveSpeed * (grounded ? 1f : AirControl);
    }

    /// <summary>Limits downward velocity to the terminal fall speed.</summary>
    /// <param name="vy">The current vertical velocity.</param>
    /// <returns>The vertical velocity after limiting the fall speed.</returns>
    public static float ClampFall(float vy)
    {
        return vy < MaxFallSpeed ? MaxFallSpeed : vy;
    }

    /// <summary>Finds the winner from the two player scores.</summary>
    /// <param name="score0">The first player's score.</param>
    /// <param name="score1">The second player's score.</param>
    /// <returns>The winning player index, or <see cref="Draw"/> for a tie.</returns>
    public static int Winner(int score0, int score1)
    {
        if (score0 > score1)
        {
            return 0;
        }
        if (score1 > score0)
        {
            return 1;
        }
        return Draw;
    }

    /// <summary>Checks whether the active match timer has expired.</summary>
    /// <param name="timeLeft">The remaining match time.</param>
    /// <returns>Whether no time remains.</returns>
    public static bool IsMatchOver(float timeLeft)
    {
        return timeLeft <= 0f;
    }

    /// <summary>Decreases a timer without allowing a negative result.</summary>
    /// <param name="timeLeft">The remaining time.</param>
    /// <param name="dt">The elapsed tick duration.</param>
    /// <returns>The remaining time, at least zero.</returns>
    public static float TickTimer(float timeLeft, float dt)
    {
        float t = timeLeft - dt;
        return t < 0f ? 0f : t;
    }

    /// <summary>Checks whether a collected coin can reappear.</summary>
    /// <param name="respawnIn">The remaining coin respawn delay.</param>
    /// <returns>Whether the respawn delay has expired.</returns>
    public static bool CoinReady(float respawnIn)
    {
        return respawnIn <= 0f;
    }

    /// <summary>Decreases a coin respawn delay without allowing a negative result.</summary>
    /// <param name="respawnIn">The remaining coin respawn delay.</param>
    /// <param name="dt">The elapsed tick duration.</param>
    /// <returns>The updated delay, at least zero.</returns>
    public static float TickCoin(float respawnIn, float dt)
    {
        if (respawnIn <= 0f)
        {
            return 0f;
        }
        float t = respawnIn - dt;
        return t < 0f ? 0f : t;
    }

    /// <summary>Interpolates between two values while clamping progress to zero through one.</summary>
    /// <param name="a">The starting value.</param>
    /// <param name="b">The ending value.</param>
    /// <param name="t">The interpolation progress.</param>
    /// <returns>The interpolated value.</returns>
    public static float Lerp(float a, float b, float t)
    {
        if (t < 0f)
        {
            t = 0f;
        }
        else if (t > 1f)
        {
            t = 1f;
        }
        return a + ((b - a) * t);
    }

    /// <summary>Calculates clamped snapshot interpolation progress.</summary>
    /// <param name="elapsed">Time since the previous snapshot.</param>
    /// <param name="interval">The expected time between snapshots.</param>
    /// <returns>The interpolation progress from zero through one.</returns>
    public static float SnapshotProgress(float elapsed, float interval)
    {
        return interval <= 0f ? 1f : Lerp(0f, 1f, elapsed / interval);
    }

    /// <summary>Calculates elapsed time between snapshot ticks.</summary>
    /// <param name="tickDelta">The number of fixed ticks between snapshots.</param>
    /// <param name="fixedDelta">The fixed tick duration.</param>
    /// <returns>The interval for at least one tick.</returns>
    public static float SnapshotInterval(int tickDelta, float fixedDelta)
    {
        return Math.Max(tickDelta, 1) * fixedDelta;
    }

    /// <summary>Checks whether a player has fallen below the level boundary.</summary>
    /// <param name="y">The player's vertical position.</param>
    /// <param name="killY">The level's lower boundary.</param>
    /// <returns>Whether the player is below the boundary.</returns>
    public static bool FellOut(float y, float killY)
    {
        return y < killY;
    }

    /// <summary>Advances a moving platform between its endpoints for one fixed tick.</summary>
    /// <param name="m">The platform movement state to update.</param>
    /// <param name="dt">The fixed tick duration.</param>
    public static void StepMover(ref MovingPlatform m, float dt)
    {
        float span = MathF.Abs(m.ToX - m.FromX) + MathF.Abs(m.ToY - m.FromY);
        if (span <= 0.0001f || m.Speed <= 0f)
        {
            return;
        }
        float step = (m.Speed * dt) / span;
        m.Phase += step * m.Direction;
        if (m.Phase >= 1f)
        {
            m.Phase = 1f;
            m.Direction = -1;
        }
        else if (m.Phase <= 0f)
        {
            m.Phase = 0f;
            m.Direction = 1;
        }
    }

    /// <summary>Calculates the moving platform position from its current phase.</summary>
    /// <param name="m">The moving platform state.</param>
    /// <param name="x">Receives the horizontal position.</param>
    /// <param name="y">Receives the vertical position.</param>
    public static void MoverPosition(in MovingPlatform m, out float x, out float y)
    {
        x = m.FromX + ((m.ToX - m.FromX) * m.Phase);
        y = m.FromY + ((m.ToY - m.FromY) * m.Phase);
    }
}
