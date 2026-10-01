using System.Text.Json;
using Karpik.Engine.Core.FileSystem;

namespace SSSuperGame.Shared.CoinRush;

/// <summary>Defines match timing, player count, and server connection settings.</summary>
/// <param name="MatchDuration">The duration of an active match in seconds.</param>
/// <param name="CountdownDuration">The countdown duration in seconds.</param>
/// <param name="CoinRespawnDelay">The delay before a collected coin returns, in seconds.</param>
/// <param name="MaxPlayers">The number of supported players.</param>
/// <param name="ServerPort">The server UDP port.</param>
/// <param name="ServerKey">The network connection key.</param>
public sealed record MatchConfig(
    float MatchDuration,
    float CountdownDuration,
    float CoinRespawnDelay,
    int MaxPlayers,
    int ServerPort,
    string ServerKey);

/// <summary>Describes a static or moving level platform.</summary>
/// <param name="X">The initial horizontal position.</param>
/// <param name="Y">The initial vertical position.</param>
/// <param name="Width">The platform width.</param>
/// <param name="Height">The platform height.</param>
/// <param name="IsMover">Whether the platform moves between two positions.</param>
/// <param name="ToX">The moving platform's destination X coordinate.</param>
/// <param name="ToY">The moving platform's destination Y coordinate.</param>
/// <param name="Speed">The moving platform speed.</param>
public sealed record LevelPlatformDto(
    float X,
    float Y,
    float Width,
    float Height,
    bool IsMover,
    float ToX,
    float ToY,
    float Speed);

/// <summary>Describes a crate spawn and its square size.</summary>
/// <param name="X">The crate's horizontal position.</param>
/// <param name="Y">The crate's vertical position.</param>
/// <param name="Size">The crate's side length.</param>
public sealed record LevelBoxDto(float X, float Y, float Size);
/// <summary>Describes a rectangular spike hazard.</summary>
/// <param name="X">The hazard's horizontal position.</param>
/// <param name="Y">The hazard's vertical position.</param>
/// <param name="Width">The hazard width.</param>
/// <param name="Height">The hazard height.</param>
public sealed record LevelHazardDto(float X, float Y, float Width, float Height);
/// <summary>Describes a checkpoint sensor.</summary>
/// <param name="X">The checkpoint's horizontal position.</param>
/// <param name="Y">The checkpoint's vertical position.</param>
/// <param name="Radius">The checkpoint radius.</param>
public sealed record LevelCheckpointDto(float X, float Y, float Radius);
/// <summary>Describes a player spawn position.</summary>
/// <param name="X">The spawn's horizontal position.</param>
/// <param name="Y">The spawn's vertical position.</param>
public sealed record LevelSpawnDto(float X, float Y);
/// <summary>Describes a coin spawn position.</summary>
/// <param name="X">The coin's horizontal position.</param>
/// <param name="Y">The coin's vertical position.</param>
public sealed record CoinSpawnDto(float X, float Y);

/// <summary>Defines the level bounds and objects loaded from level content.</summary>
/// <param name="KillY">The vertical threshold below which players respawn.</param>
/// <param name="PlayerSpawns">The spawn position for each player slot.</param>
/// <param name="Platforms">The platforms in the level.</param>
/// <param name="Crates">The crates in the level.</param>
/// <param name="Spikes">The spike hazards in the level.</param>
/// <param name="Checkpoints">The checkpoints in the level.</param>
public sealed record LevelConfig(
    float KillY,
    LevelSpawnDto[] PlayerSpawns,
    LevelPlatformDto[] Platforms,
    LevelBoxDto[] Crates,
    LevelHazardDto[] Spikes,
    LevelCheckpointDto[] Checkpoints);

/// <summary>Lists coin spawn positions loaded from content.</summary>
/// <param name="Coins">The coin spawn positions.</param>
public sealed record CoinSpawnsConfig(CoinSpawnDto[] Coins);

/// <summary>Defines collision categories and masks loaded from content.</summary>
/// <param name="Player">The player collision category.</param>
/// <param name="Platform">The platform collision category.</param>
/// <param name="Crate">The crate collision category.</param>
/// <param name="Sensor">The sensor collision category.</param>
/// <param name="PlayerMask">The categories players can collide with.</param>
/// <param name="CrateMask">The categories crates can collide with.</param>
public sealed record PhysicsLayersDoc(
    uint Player,
    uint Platform,
    uint Crate,
    uint Sensor,
    uint PlayerMask,
    uint CrateMask);

/// <summary>Contains localized labels and hints shown by the client.</summary>
/// <param name="Title">The game title.</param>
/// <param name="Waiting">The waiting-for-players message.</param>
/// <param name="Countdown">The countdown message.</param>
/// <param name="Finished">The match-finished message.</param>
/// <param name="Draw">The draw result label.</param>
/// <param name="Player0">The first player label.</param>
/// <param name="Player1">The second player label.</param>
/// <param name="ControlsHint">The movement controls hint.</param>
/// <param name="RestartHint">The restart controls hint.</param>
/// <param name="NewRoundHint">The next-round controls hint.</param>
public sealed record UiStrings(
    string Title,
    string Waiting,
    string Countdown,
    string Finished,
    string Draw,
    string Player0,
    string Player1,
    string ControlsHint,
    string RestartHint,
    string NewRoundHint);

/// <summary>Loads and validates CoinRush content used by both game sides.</summary>
public static class CoinRushContent
{
    /// <summary>Reads match settings from the raw content file, using defaults when it is absent.</summary>
    /// <param name="fs">The file system that exposes the content directory.</param>
    /// <returns>The validated match settings.</returns>
    /// <exception cref="InvalidDataException">The file is empty or contains invalid match settings.</exception>
    public static MatchConfig LoadMatch(IFileSystem fs)
    {
        string path = fs.Combine(fs.ContentPath, "CoinRush", "Match.json");
        if (!fs.Exists(path)) return DefaultMatch();
        using Stream input = fs.OpenRead(path);
        MatchConfig match = JsonSerializer.Deserialize<MatchConfig>(input)
            ?? throw new InvalidDataException("CoinRush match configuration is empty.");
        ValidateMatch(match);
        return match;
    }

    /// <summary>Checks that match settings are valid for the two-player game mode.</summary>
    /// <param name="match">The settings to validate.</param>
    /// <exception cref="InvalidDataException">A required setting is invalid.</exception>
    public static void ValidateMatch(MatchConfig match)
    {
        if (match.MaxPlayers != MatchRules.MaxPlayers ||
            !float.IsFinite(match.MatchDuration) || match.MatchDuration <= 0f ||
            !float.IsFinite(match.CountdownDuration) || match.CountdownDuration <= 0f ||
            !float.IsFinite(match.CoinRespawnDelay) || match.CoinRespawnDelay < 0f ||
            match.ServerPort is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(match.ServerKey))
        {
            throw new InvalidDataException("Invalid CoinRush match configuration (this mode requires exactly two players).");
        }
    }

    /// <summary>Checks the kill height and player spawn positions.</summary>
    /// <param name="level">The level settings to validate.</param>
    /// <exception cref="InvalidDataException">The kill height or a player spawn is invalid.</exception>
    public static void ValidateLevel(LevelConfig level)
    {
        if (!float.IsFinite(level.KillY) || level.PlayerSpawns?.Length != MatchRules.MaxPlayers)
        {
            throw new InvalidDataException("CoinRush level requires a finite kill height and two player spawns.");
        }
        foreach (LevelSpawnDto spawn in level.PlayerSpawns)
        {
            if (spawn is null || !float.IsFinite(spawn.X) || !float.IsFinite(spawn.Y))
            {
                throw new InvalidDataException("CoinRush player spawn coordinates must be finite.");
            }
        }
    }

    public const string MatchPath = "game/CoinRush/Match";
    public const string LevelPath = "game/CoinRush/Level";
    public const string CoinSpawnsPath = "game/CoinRush/CoinSpawns";
    public const string PhysicsLayersPath = "game/CoinRush/PhysicsLayers";
    public const string UiStringsPath = "game/CoinRush/UiStrings";

    /// <summary>Creates built-in match settings used when the content file is absent.</summary>
    /// <returns>The default match settings.</returns>
    public static MatchConfig DefaultMatch() => new(
        MatchDuration: MatchRules.MatchDuration,
        CountdownDuration: MatchRules.CountdownDuration,
        CoinRespawnDelay: MatchRules.CoinRespawnDelay,
        MaxPlayers: MatchRules.MaxPlayers,
        ServerPort: 14789,
        ServerKey: "coinrush");

    /// <summary>Creates built-in English UI labels and control hints.</summary>
    /// <returns>The default UI strings.</returns>
    public static UiStrings DefaultUi() => new(
        Title: "COIN RUSH",
        Waiting: "Waiting for players (need 2)...",
        Countdown: "Get ready!",
        Finished: "Time!",
        Draw: "Draw!",
        Player0: "P1",
        Player1: "P2",
        ControlsHint: "A/D or Arrows: move, Space/W/Up: jump",
        RestartHint: "R: request restart (debug)",
        NewRoundHint: "Press Enter for next round");
}
