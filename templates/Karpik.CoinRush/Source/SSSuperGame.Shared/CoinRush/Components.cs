using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.DragonECS;

namespace SSSuperGame.Shared.CoinRush;

/// <summary>Identifies a controllable player and stores its replicated score and display color.</summary>
[NetworkedComponent]
public struct Player : IEcsComponent
{
    [NetworkedField] public int Index;
    [NetworkedField] public int Score;
    public int PeerId;
    public byte R;
    public byte G;
    public byte B;
}

/// <summary>Stores server-side movement input, jump timing, and fallback physics state.</summary>
public struct MoveState : IEcsComponent
{
    public float MoveAxis;
    public float CoyoteTimer;
    public float BufferTimer;
    public float FallbackVelY;
    public byte Grounded;
    public byte JumpPressedLatched;
    public int LastInputSequence;
}

/// <summary>Stores the authoritative player position sent to clients.</summary>
[NetworkedComponent]
public struct NetState : IEcsComponent
{
    [NetworkedField] public float X;
    [NetworkedField] public float Y;
}

/// <summary>Stores a coin pickup's spawn position and respawn state.</summary>
[NetworkedComponent]
public struct Coin : IEcsComponent
{
    [NetworkedField] public int SpawnIndex;
    [NetworkedField] public float X;
    [NetworkedField] public float Y;
    [NetworkedField] public byte Active;
    public float RespawnIn;
}

/// <summary>Marks a platform body and stores its dimensions.</summary>
public struct PlatformTag : IEcsComponent
{
    public float Width;
    public float Height;
}

/// <summary>Stores the endpoints, movement phase, and dimensions of a platform mover.</summary>
public struct MovingPlatform : IEcsComponent
{
    public float FromX;
    public float FromY;
    public float ToX;
    public float ToY;
    public float Speed;
    public float Phase;
    public int Direction;
    public float Width;
    public float Height;
}

/// <summary>Marks a dynamic pushable crate and stores its dimensions.</summary>
public struct Crate : IEcsComponent
{
    public float Width;
    public float Height;
}

/// <summary>Marks a spike sensor that kills a touching player.</summary>
public struct Spike : IEcsComponent
{
    public float Width;
    public float Height;
}

/// <summary>Marks a checkpoint sensor that can become a player's respawn anchor.</summary>
public struct Checkpoint : IEcsComponent
{
    public int ZoneIndex;
    public float X;
    public float Y;
    public float Radius;
}

/// <summary>Stores a player's current checkpoint and original spawn position.</summary>
public struct RespawnAnchor : IEcsComponent
{
    public float X;
    public float Y;
    public float SpawnX;
    public float SpawnY;
}

/// <summary>Stores the authoritative match phase, scores, timers, and client snapshot status.</summary>
[NetworkedComponent]
public struct MatchState : IEcsComponent
{
    [NetworkedField] public byte NetworkPhase;
    public MatchPhase Phase;
    [NetworkedField] public float TimeLeft;
    [NetworkedField] public int Score0;
    [NetworkedField] public int Score1;
    [NetworkedField] public int Winner;
    [NetworkedField] public int Tick;
    [NetworkedField] public int ConnectedPeers;
    public float MatchDuration;
    public float CountdownDuration;
    public float CoinRespawnDelay;
    public float KillY;
    public float SnapshotAge;
    public int SnapTick;
    public int BroadcastCountdown;
    public bool LoggedNoBodies;
}

/// <summary>Identifies the current stage of a match.</summary>
public enum MatchPhase : byte
{
    WaitingForPlayers = 0,
    Countdown = 1,
    Running = 2,
    Finished = 3,
}

/// <summary>Stores the latest accepted input and pending button presses for one player.</summary>
public struct InputLatch : IEcsComponent
{
    public int Sequence;
    public float MoveAxis;
    public byte Jump;
    public byte Restart;
    public byte HasInput;
}

/// <summary>Keeps a server transport peer and its initial-state retry count on a player entity.</summary>
public struct ServerPeerState : IEcsComponent
{
    public IPeer? Peer;
    public int FullStateResend;
}

/// <summary>Identifies the player slot controlled by the local client.</summary>
public struct LocalPlayer : IEcsComponent
{
    public int Slot;
}

/// <summary>Keeps client connection, snapshot, and diagnostic state in the ECS world.</summary>
public struct ClientSessionState : IEcsComponent
{
    public int Sequence;
    public double LastConnectAttempt;
    public bool HasSnapshot;
    public double ReceivedAt;
    public int PreviousTick;
    public int LatestTick;
    public int PendingLocalSlot;
    public double LastLog;
    public bool WasF1;
}

/// <summary>Stores a player's interpolated display position and animation pulse.</summary>
public struct DisplayState : IEcsComponent
{
    public float X;
    public float Y;
    public float PrevX;
    public float PrevY;
    public float PreviousSnapshotX;
    public float PreviousSnapshotY;
    public float ScalePulse;
}

/// <summary>Requests a short-lived client presentation effect.</summary>
public struct FxEvent : IEcsComponentEvent
{
    /// <summary>The source entity identifier.</summary>
    public int Source { get; set; }
    /// <summary>The target entity identifier.</summary>
    public int Target { get; set; }
    public FxKind Kind;
    public float X;
    public float Y;
    public int PlayerIndex;
    public float Age;
    public float Duration;
}

/// <summary>Identifies the client presentation effect to display.</summary>
public enum FxKind : byte
{
    CoinPickup = 0,
    Death = 1,
    Checkpoint = 2,
    MatchEnd = 3,
}
