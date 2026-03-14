using System.Numerics;
using DCFApixels.DragonECS;

namespace Karpik.Engine.MyGame.Shared.Main;

/// <summary>
/// Collectible (coin, gem, etc.)
/// </summary>
public struct Collectible : IEcsComponent
{
    public int ScoreValue;
    public bool IsCollected;
}

/// <summary>
/// Finish zone - player wins when reaches this
/// </summary>
public struct FinishZone : IEcsComponent;

/// <summary>
/// Death zone - player dies when touches this (spikes, lava, etc.)
/// </summary>
public struct DeathZone : IEcsComponent;

/// <summary>
/// Respawn point where player spawns
/// </summary>
public struct RespawnPoint : IEcsComponent;