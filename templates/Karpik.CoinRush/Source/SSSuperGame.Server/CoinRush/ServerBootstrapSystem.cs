using System.Text.Json;
using DCFApixels.DragonECS;
using Karpik.Content.Generated;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Engine.Shared.Spatial2D;
using Karpik.Engine.Shared.Network.Core;
using Microsoft.Extensions.Logging;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Server.CoinRush;

/// <summary>Loads server content and creates or restores the match world.</summary>
/// <param name="world">The authoritative entity world.</param>
/// <param name="content">The runtime content registry.</param>
/// <param name="store">The content store for manifest assets.</param>
/// <param name="fs">The file system containing game content.</param>
/// <param name="config">The network settings loaded before system initialization.</param>
/// <param name="log">The logger for content and bootstrap status.</param>
public sealed class ServerBootstrapSystem(
    EcsDefaultWorld world,
    IContentRegistry content,
    IContentStore store,
    Karpik.Engine.Core.FileSystem.IFileSystem fs,
    NetworkConfig config,
    ILogger<ServerBootstrapSystem> log)
    : ISystemAsyncInit
{
    /// <summary>Loads match content and initializes entities, or updates settings after hot reload.</summary>
    /// <param name="ct">The cancellation token for content loading.</param>
    /// <returns>A task that completes when the world is ready.</returns>
    /// <exception cref="InvalidDataException">Source and cooked network settings differ.</exception>
    public async ValueTask InitAsync(CancellationToken ct)
    {
        ContentBootstrap.EnsureManifest(content, store, fs, log, "server");
        MatchConfig match = await LoadJson(ContentRefs.Game_CoinRush_Match, CoinRushContent.DefaultMatch(), ct)
            ?? CoinRushContent.DefaultMatch();
        LevelConfig? level = await LoadJson<LevelConfig>(ContentRefs.Game_CoinRush_Level, null, ct);
        CoinSpawnsConfig? coins = await LoadJson<CoinSpawnsConfig>(ContentRefs.Game_CoinRush_CoinSpawns, null, ct);
        CoinRushContent.ValidateMatch(match);
        if (level is not null) CoinRushContent.ValidateLevel(level);
        if (config.Port != match.ServerPort || config.Key != match.ServerKey)
            throw new InvalidDataException("CoinRush network settings differ between source and cooked content.");

        log.LogInformation(
            "CoinRush server config: match {Dur}s, port {Port}, level platforms {P}, coins {C}",
            match.MatchDuration, match.ServerPort,
            level?.Platforms?.Length ?? LevelData.Platforms.Length,
            coins?.Coins?.Length ?? LevelData.Coins.Length);

        if (HasMatch())
        {
            ConfigureExisting(match, level);
            log.LogInformation("Match state survived hot reload, skipping respawn.");
            return;
        }

        SpawnMatch(match, level);
        SpawnLevel(level);
        SpawnCoins(coins);
        SpawnPlayers(level);
    }

    /// <summary>Checks whether match state already exists after a worker restart.</summary>
    /// <returns><see langword="true"/> when a match entity exists.</returns>
    private bool HasMatch()
    {
        return world.GetPool<MatchState>().Count > 0;
    }

    /// <summary>Loads and deserializes a JSON content asset, returning a fallback on failure.</summary>
    /// <typeparam name="T">The target content DTO type.</typeparam>
    /// <param name="asset">The JSON asset reference.</param>
    /// <param name="fallback">The value to use when loading fails.</param>
    /// <param name="ct">The cancellation token for loading.</param>
    /// <returns>The deserialized value or the supplied fallback.</returns>
    private async ValueTask<T?> LoadJson<T>(AssetRef<RawJsonPayload> asset, T? fallback, CancellationToken ct) where T : class
    {
        await Task.Yield();
        try
        {
            bool aliveBefore = content.IsAlive(asset);
            await content.LoadAsync(asset, ct);
            bool aliveAfter = content.IsAlive(asset);
            bool got = content.TryGet(asset, out AssetLease<RawJsonPayload> lease);
            using (lease)
            {
                string? slotJson = lease.Payload?.Json;
                log.LogInformation("Content slot {Asset}: alive {B}->{A}, got {G}, bytes {N}.",
                    asset.ToCanonicalString(), aliveBefore, aliveAfter, got, slotJson?.Length ?? -1);
                if (!string.IsNullOrEmpty(slotJson))
                {
                    return JsonSerializer.Deserialize<T>(slotJson);
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Content load failed, using code fallback.");
        }
        return fallback;
    }

    /// <summary>Copies match and level settings into ECS match state.</summary>
    /// <param name="state">The state to configure.</param>
    /// <param name="match">The loaded match configuration.</param>
    /// <param name="level">The loaded level configuration, if available.</param>
    private static void ConfigureMatch(ref MatchState state, MatchConfig match, LevelConfig? level)
    {
        state.MatchDuration = match.MatchDuration;
        state.CountdownDuration = match.CountdownDuration;
        state.CoinRespawnDelay = match.CoinRespawnDelay;
        state.KillY = level?.KillY ?? LevelData.KillY;
    }

    /// <summary>Refreshes match settings and base spawn points after hot reload.</summary>
    /// <param name="match">The loaded match configuration.</param>
    /// <param name="level">The loaded level configuration, if available.</param>
    private void ConfigureExisting(MatchConfig match, LevelConfig? level)
    {
        var matches = world.GetPool<MatchState>();
        var players = world.GetPool<Player>();
        var anchors = world.GetPool<RespawnAnchor>();
        foreach (int e in world.Entities)
        {
            if (matches.Has(e)) ConfigureMatch(ref matches.Get(e), match, level);
            if (!players.Has(e) || !anchors.Has(e)) continue;
            int index = players.Read(e).Index;
            if ((uint)index >= MatchRules.MaxPlayers) continue;
            LevelSpawnDto? spawn = level?.PlayerSpawns[index];
            ref RespawnAnchor anchor = ref anchors.Get(e);
            anchor.SpawnX = spawn?.X ?? LevelData.PlayerSpawns[index].X;
            anchor.SpawnY = spawn?.Y ?? LevelData.PlayerSpawns[index].Y;
        }
    }

    /// <summary>Creates the authoritative match entity.</summary>
    /// <param name="match">The loaded match configuration.</param>
    /// <param name="level">The loaded level configuration, if available.</param>
    private void SpawnMatch(MatchConfig match, LevelConfig? level)
    {
        int e = world.NewEntity();
        world.GetPool<NetworkId>().Add(e) = new NetworkId { Id = e };
        world.GetPool<MatchState>().Add(e) = new MatchState
        {
            Phase = MatchPhase.WaitingForPlayers,
            TimeLeft = match.MatchDuration,
            Score0 = 0,
            Score1 = 0,
            Winner = MatchRules.Draw,
            Tick = 0,
            ConnectedPeers = 0,
        };
        ref MatchState state = ref world.GetPool<MatchState>().Get(e);
        ConfigureMatch(ref state, match, level);
    }

    /// <summary>Creates a default static physics body configuration.</summary>
    /// <param name="category">The collision category bits.</param>
    /// <param name="mask">The collision mask bits.</param>
    /// <returns>A static body configuration.</returns>
    private static BodyConfig StaticBody(uint category, uint mask) => new()
    {
        Type = BodyType.Static,
        Mass = 0,
        Friction = 0.6,
        Restitution = 0,
        IsSensor = false,
        IgnoreGravity = false,
        CategoryBits = category,
        MaskBits = mask,
    };

    /// <summary>Adds a static rectangular body to the world.</summary>
    /// <param name="x">The horizontal center.</param>
    /// <param name="y">The vertical center.</param>
    /// <param name="w">The width.</param>
    /// <param name="h">The height.</param>
    /// <param name="category">The collision category bits.</param>
    /// <param name="mask">The collision mask bits.</param>
    /// <param name="sensor">Whether the body is a sensor.</param>
    private void AddStaticBox(float x, float y, float w, float h, uint category, uint mask, bool sensor)
    {
        int e = world.NewEntity();
        world.GetPool<Transform2D>().Add(e) = new Transform2D
        {
            Position = new OpenTK.Mathematics.Vector2d(x, y),
            Rotation = 0f,
            Scale = new OpenTK.Mathematics.Vector2(1f, 1f),
        };
        var body = StaticBody(category, mask);
        body.IsSensor = sensor;
        var shape = ShapeConfig.Box(new OpenTK.Mathematics.Vector2d(w, h));
        world.GetPool<PhysicsBodyDefinition>().Add(e) = new PhysicsBodyDefinition
        {
            BodyConfig = body,
            ShapeConfig = shape,
        };
        world.GetPool<CreateBodyRequest>().Add(e) = new CreateBodyRequest
        {
            BodyConfig = body,
            ShapeConfig = shape,
        };
    }

    /// <summary>Creates platforms, crates, spikes, and checkpoints from level data.</summary>
    /// <param name="level">The loaded level, or <see langword="null"/> for built-in data.</param>
    private void SpawnLevel(LevelConfig? level)
    {
        if (level?.Platforms != null)
        {
            foreach (LevelPlatformDto p in level.Platforms)
            {
                SpawnPlatform(p.X, p.Y, p.Width, p.Height, p.IsMover, p.ToX, p.ToY, p.Speed);
            }
            foreach (LevelBoxDto b in level.Crates ?? Array.Empty<LevelBoxDto>())
            {
                SpawnCrate(b.X, b.Y, b.Size);
            }
            foreach (LevelHazardDto s in level.Spikes ?? Array.Empty<LevelHazardDto>())
            {
                SpawnSpike(s.X, s.Y, s.Width, s.Height);
            }
            foreach (LevelCheckpointDto c in level.Checkpoints ?? Array.Empty<LevelCheckpointDto>())
            {
                SpawnCheckpoint(c.X, c.Y, c.Radius);
            }
            return;
        }

        foreach (PlatformDesc p in LevelData.Platforms)
        {
            SpawnPlatform(p.X, p.Y, p.Width, p.Height, p.IsMover == 1, p.ToX, p.ToY, p.Speed);
        }
        foreach (BoxDesc b in LevelData.Crates)
        {
            SpawnCrate(b.X, b.Y, b.Size);
        }
        foreach (HazardDesc s in LevelData.Spikes)
        {
            SpawnSpike(s.X, s.Y, s.Width, s.Height);
        }
        for (int i = 0; i < LevelData.Checkpoints.Length; i++)
        {
            CheckpointDesc c = LevelData.Checkpoints[i];
            int e = world.NewEntity();
            world.GetPool<Checkpoint>().Add(e) = new Checkpoint { ZoneIndex = i, X = c.X, Y = c.Y, Radius = c.Radius };
            AddStaticBox(c.X, c.Y, c.Radius * 2f, 0.4f, CoinRushLayers.Sensor, CoinRushLayers.SensorMask, sensor: true);
        }
    }

    /// <summary>Creates a stationary or moving platform.</summary>
    /// <param name="x">The initial horizontal center.</param>
    /// <param name="y">The initial vertical center.</param>
    /// <param name="w">The width.</param>
    /// <param name="h">The height.</param>
    /// <param name="mover">Whether the platform moves.</param>
    /// <param name="toX">The horizontal movement target.</param>
    /// <param name="toY">The vertical movement target.</param>
    /// <param name="speed">The movement speed.</param>
    private void SpawnPlatform(float x, float y, float w, float h, bool mover, float toX, float toY, float speed)
    {
        int e = world.NewEntity();
        world.GetPool<Transform2D>().Add(e) = new Transform2D
        {
            Position = new OpenTK.Mathematics.Vector2d(x, y),
            Rotation = 0f,
            Scale = new OpenTK.Mathematics.Vector2(1f, 1f),
        };
        world.GetPool<PlatformTag>().Add(e) = new PlatformTag { Width = w, Height = h };
        if (mover)
        {
            world.GetPool<MovingPlatform>().Add(e) = new MovingPlatform
            {
                FromX = x, FromY = y, ToX = toX, ToY = toY,
                Speed = speed <= 0f ? 1.6f : speed, Phase = 0f, Direction = 1,
                Width = w, Height = h,
            };
            var kine = StaticBody(CoinRushLayers.Platform, CoinRushLayers.PlatformMask);
            kine.Type = BodyType.Kinematic;
            var kshape = ShapeConfig.Box(new OpenTK.Mathematics.Vector2d(w, h));
            world.GetPool<PhysicsBodyDefinition>().Add(e) = new PhysicsBodyDefinition { BodyConfig = kine, ShapeConfig = kshape };
            world.GetPool<CreateBodyRequest>().Add(e) = new CreateBodyRequest { BodyConfig = kine, ShapeConfig = kshape };
            return;
        }
        var body = StaticBody(CoinRushLayers.Platform, CoinRushLayers.PlatformMask);
        var shape = ShapeConfig.Box(new OpenTK.Mathematics.Vector2d(w, h));
        world.GetPool<PhysicsBodyDefinition>().Add(e) = new PhysicsBodyDefinition { BodyConfig = body, ShapeConfig = shape };
        world.GetPool<CreateBodyRequest>().Add(e) = new CreateBodyRequest { BodyConfig = body, ShapeConfig = shape };
    }

    /// <summary>Creates a dynamic crate.</summary>
    /// <param name="x">The horizontal center.</param>
    /// <param name="y">The vertical center.</param>
    /// <param name="size">The crate width and height.</param>
    private void SpawnCrate(float x, float y, float size)
    {
        int e = world.NewEntity();
        world.GetPool<Transform2D>().Add(e) = new Transform2D
        {
            Position = new OpenTK.Mathematics.Vector2d(x, y),
            Rotation = 0f,
            Scale = new OpenTK.Mathematics.Vector2(1f, 1f),
        };
        world.GetPool<Crate>().Add(e) = new Crate { Width = size, Height = size };
        world.GetPool<Velocity2D>().Add(e) = default;
        var body = new BodyConfig
        {
            Type = BodyType.Dynamic,
            Mass = 4,
            Friction = 0.5,
            Restitution = 0.05,
            IsSensor = false,
            IgnoreGravity = false,
            CategoryBits = CoinRushLayers.Crate,
            MaskBits = CoinRushLayers.CrateMask,
        };
        var shape = ShapeConfig.Box(new OpenTK.Mathematics.Vector2d(size, size));
        world.GetPool<PhysicsBodyDefinition>().Add(e) = new PhysicsBodyDefinition { BodyConfig = body, ShapeConfig = shape };
        world.GetPool<CreateBodyRequest>().Add(e) = new CreateBodyRequest { BodyConfig = body, ShapeConfig = shape };
    }

    /// <summary>Creates a spike hazard and its sensor body.</summary>
    /// <param name="x">The horizontal center.</param>
    /// <param name="y">The vertical center.</param>
    /// <param name="w">The width.</param>
    /// <param name="h">The height.</param>
    private void SpawnSpike(float x, float y, float w, float h)
    {
        int e = world.NewEntity();
        world.GetPool<Spike>().Add(e) = new Spike { Width = w, Height = h };
        AddStaticBox(x, y, w, h, CoinRushLayers.Sensor, CoinRushLayers.SensorMask, sensor: true);
        world.GetPool<Transform2D>().Add(e) = new Transform2D
        {
            Position = new OpenTK.Mathematics.Vector2d(x, y),
            Rotation = 0f,
            Scale = new OpenTK.Mathematics.Vector2(1f, 1f),
        };
    }

    /// <summary>Creates a checkpoint zone and its sensor body.</summary>
    /// <param name="x">The horizontal center.</param>
    /// <param name="y">The vertical center.</param>
    /// <param name="radius">The checkpoint activation radius.</param>
    private void SpawnCheckpoint(float x, float y, float radius)
    {
        int e = world.NewEntity();
        world.GetPool<Checkpoint>().Add(e) = new Checkpoint { ZoneIndex = 0, X = x, Y = y, Radius = radius };
        AddStaticBox(x, y, radius * 2f, 0.4f, CoinRushLayers.Sensor, CoinRushLayers.SensorMask, sensor: true);
    }

    /// <summary>Creates collectible coins from content or built-in spawn data.</summary>
    /// <param name="coins">The loaded coin spawns, if available.</param>
    private void SpawnCoins(CoinSpawnsConfig? coins)
    {
        CoinSpawnDto[] list = coins?.Coins ?? ToDto(LevelData.Coins);
        int n = list.Length;
        for (int i = 0; i < n; i++)
        {
            int e = world.NewEntity();
            world.GetPool<NetworkId>().Add(e) = new NetworkId { Id = e };
            world.GetPool<Coin>().Add(e) = new Coin
            {
                SpawnIndex = i,
                X = list[i].X,
                Y = list[i].Y,
                Active = 1,
                RespawnIn = 0f,
            };
            var body = StaticBody(CoinRushLayers.Sensor, CoinRushLayers.SensorMask);
            body.IsSensor = true;
            body.IgnoreGravity = true;
            var shape = ShapeConfig.Circle(0.45);
            world.GetPool<Transform2D>().Add(e) = new Transform2D
            {
                Position = new OpenTK.Mathematics.Vector2d(list[i].X, list[i].Y),
                Rotation = 0f,
                Scale = new OpenTK.Mathematics.Vector2(1f, 1f),
            };
            world.GetPool<PhysicsBodyDefinition>().Add(e) = new PhysicsBodyDefinition { BodyConfig = body, ShapeConfig = shape };
            world.GetPool<CreateBodyRequest>().Add(e) = new CreateBodyRequest { BodyConfig = body, ShapeConfig = shape };
        }
    }

    /// <summary>Converts built-in coin spawn descriptions to content DTOs.</summary>
    /// <param name="src">The built-in coin spawns.</param>
    /// <returns>Equivalent coin spawn DTOs.</returns>
    private static CoinSpawnDto[] ToDto(CoinSpawnDesc[] src)
    {
        var dst = new CoinSpawnDto[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            dst[i] = new CoinSpawnDto(src[i].X, src[i].Y);
        }
        return dst;
    }

    /// <summary>Creates player entities and their physical bodies at level spawn points.</summary>
    /// <param name="level">The loaded level, if available.</param>
    private void SpawnPlayers(LevelConfig? level)
    {
        for (int i = 0; i < MatchRules.MaxPlayers; i++)
        {
            LevelSpawnDto? fromLevel = level?.PlayerSpawns[i];
            float x = fromLevel?.X ?? LevelData.PlayerSpawns[i].X;
            float y = fromLevel?.Y ?? LevelData.PlayerSpawns[i].Y;
            LevelData.PlayerColor(i, out byte r, out byte g, out byte b);
            int e = world.NewEntity();
            world.GetPool<NetworkId>().Add(e) = new NetworkId { Id = e };
            world.GetPool<Player>().Add(e) = new Player { Index = i, Score = 0, PeerId = -1, R = r, G = g, B = b };
            world.GetPool<MoveState>().Add(e) = new MoveState { LastInputSequence = -1 };
            world.GetPool<NetState>().Add(e) = new NetState { X = x, Y = y };
            world.GetPool<RespawnAnchor>().Add(e) = new RespawnAnchor { X = x, Y = y, SpawnX = x, SpawnY = y };
            world.GetPool<Transform2D>().Add(e) = new Transform2D
            {
                Position = new OpenTK.Mathematics.Vector2d(x, y),
                Rotation = 0f,
                Scale = new OpenTK.Mathematics.Vector2(1f, 1f),
            };
            world.GetPool<Velocity2D>().Add(e) = default;
            var body = new BodyConfig
            {
                Type = BodyType.Dynamic,
                Mass = 1,
                Friction = 0.35,
                Restitution = 0,
                IsSensor = false,
                IgnoreGravity = false,
                CategoryBits = CoinRushLayers.Player,
                MaskBits = CoinRushLayers.PlayerMask,
            };
            var shape = ShapeConfig.Box(new OpenTK.Mathematics.Vector2d(0.9, 1.7));
            world.GetPool<PhysicsBodyDefinition>().Add(e) = new PhysicsBodyDefinition { BodyConfig = body, ShapeConfig = shape };
            world.GetPool<CreateBodyRequest>().Add(e) = new CreateBodyRequest { BodyConfig = body, ShapeConfig = shape };
        }
    }
}
