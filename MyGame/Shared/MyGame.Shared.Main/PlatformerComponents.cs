using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Network.Core;

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

[Serializable]
[NetworkedComponent]
public struct SpriteData : IEcsComponent
{
    [NetworkedField]
    public Color Color;
    [NetworkedField]
    public string TexturePath;
    [NetworkedField]
    public Vector2 Size;
}

public struct IgnoreSpriteData : IEcsComponent;