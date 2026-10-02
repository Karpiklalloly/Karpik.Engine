namespace SSSuperGame.Shared.CoinRush;

/// <summary>Defines fallback collision categories and masks for CoinRush bodies.</summary>
public static class CoinRushLayers
{
    public const uint Player = 1u << 0;
    public const uint Platform = 1u << 1;
    public const uint Crate = 1u << 2;
    public const uint Sensor = 1u << 3;

    public const uint PlayerMask = Platform | Crate | Sensor;
    public const uint CrateMask = Platform | Player | Crate | Sensor;
    public const uint SensorMask = Player | Crate;
    public const uint PlatformMask = unchecked((uint)-1);
}

/// <summary>Describes a platform in the built-in fallback level.</summary>
public struct PlatformDesc
{
    public float X;
    public float Y;
    public float Width;
    public float Height;
    public byte IsMover;
    public float ToX;
    public float ToY;
    public float Speed;
}

/// <summary>Describes a square crate in the fallback level.</summary>
public struct BoxDesc
{
    public float X;
    public float Y;
    public float Size;
}

/// <summary>Describes a rectangular spike hazard in the fallback level.</summary>
public struct HazardDesc
{
    public float X;
    public float Y;
    public float Width;
    public float Height;
}

/// <summary>Describes a checkpoint in the fallback level.</summary>
public struct CheckpointDesc
{
    public float X;
    public float Y;
    public float Radius;
}

/// <summary>Describes a coin spawn in the fallback level.</summary>
public struct CoinSpawnDesc
{
    public float X;
    public float Y;
}

/// <summary>Describes a player spawn in the fallback level.</summary>
public struct SpawnDesc
{
    public float X;
    public float Y;
}

/// <summary>Provides the built-in fallback level layout and player colors.</summary>
public static class LevelData
{
    public const int CoinCount = 12;
    public const float KillY = -12f;
    public const float WorldMinX = -16f;
    public const float WorldMaxX = 16f;

    public static readonly SpawnDesc[] PlayerSpawns =
    {
        new() { X = -11f, Y = 2f },
        new() { X = 11f, Y = 2f },
    };

    public static readonly PlatformDesc[] Platforms =
    {
        new() { X = 0f, Y = -2f, Width = 30f, Height = 1f },
        new() { X = -10f, Y = 1.5f, Width = 6f, Height = 0.6f },
        new() { X = 10f, Y = 1.5f, Width = 6f, Height = 0.6f },
        new() { X = 0f, Y = 4.5f, Width = 8f, Height = 0.6f },
        new() { X = -4f, Y = 7.5f, Width = 4f, Height = 0.5f },
        new() { X = 4f, Y = 7.5f, Width = 4f, Height = 0.5f },
        new() { X = 0f, Y = 7.5f, Width = 3f, Height = 0.5f, IsMover = 1, ToX = 0f, ToY = 4.5f, Speed = 1.6f },
    };

    public static readonly BoxDesc[] Crates =
    {
        new() { X = -3f, Y = 0f, Size = 1.1f },
        new() { X = 3f, Y = 0f, Size = 1.1f },
        new() { X = 0f, Y = 5.6f, Size = 1f },
    };

    public static readonly HazardDesc[] Spikes =
    {
        new() { X = -6.5f, Y = -1.2f, Width = 2.4f, Height = 0.5f },
        new() { X = 6.5f, Y = -1.2f, Width = 2.4f, Height = 0.5f },
    };

    public static readonly CheckpointDesc[] Checkpoints =
    {
        new() { X = -10f, Y = 2.6f, Radius = 1.2f },
        new() { X = 10f, Y = 2.6f, Radius = 1.2f },
        new() { X = 0f, Y = 5.6f, Radius = 1.2f },
    };

    public static readonly CoinSpawnDesc[] Coins =
    {
        new() { X = -10f, Y = 3.2f },
        new() { X = -6f, Y = 0.5f },
        new() { X = -3f, Y = 2.2f },
        new() { X = 0f, Y = 6f },
        new() { X = 3f, Y = 2.2f },
        new() { X = 6f, Y = 0.5f },
        new() { X = 10f, Y = 3.2f },
        new() { X = -4f, Y = 8.6f },
        new() { X = 4f, Y = 8.6f },
        new() { X = 0f, Y = 1f },
        new() { X = -13f, Y = 1f },
        new() { X = 13f, Y = 1f },
    };

    /// <summary>Selects the display color for a player slot.</summary>
    /// <param name="index">The player slot index.</param>
    /// <param name="r">Receives the red channel.</param>
    /// <param name="g">Receives the green channel.</param>
    /// <param name="b">Receives the blue channel.</param>
    public static void PlayerColor(int index, out byte r, out byte g, out byte b)
    {
        if (index == 0)
        {
            r = 90; g = 160; b = 255;
        }
        else
        {
            r = 255; g = 140; b = 80;
        }
    }
}
