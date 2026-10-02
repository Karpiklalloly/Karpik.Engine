using System.Text.Json;
using DCFApixels.DragonECS;
using Karpik.Content.Generated;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Microsoft.Extensions.Logging;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Loads client match content and creates local display entities during initialization.</summary>
/// <param name="world">Client display world.</param>
/// <param name="content">Registry for cooked content references.</param>
/// <param name="store">Store containing cooked content.</param>
/// <param name="fs">File system used to find the content manifest.</param>
/// <param name="config">Network settings to compare with cooked match content.</param>
/// <param name="log">Logger for content loading diagnostics.</param>
public sealed class ClientBootstrapSystem(
    EcsDefaultWorld world,
    IContentRegistry content,
    IContentStore store,
    Karpik.Engine.Core.FileSystem.IFileSystem fs,
    NetworkConfig config,
    ILogger<ClientBootstrapSystem> log)
    : ISystemAsyncInit
{
    /// <summary>Registers content, validates match settings, and creates level visuals.</summary>
    /// <param name="ct">Cancellation token for content loading.</param>
    /// <exception cref="InvalidDataException">Source network settings differ from cooked match settings.</exception>
    public async ValueTask InitAsync(CancellationToken ct)
    {
        ContentBootstrap.EnsureManifest(content, store, fs, log, "client");
        MatchConfig match = await LoadJson<MatchConfig>(ContentRefs.Game_CoinRush_Match, ct)
            ?? CoinRushContent.DefaultMatch();
        CoinRushContent.ValidateMatch(match);
        if (config.Port != match.ServerPort || config.Key != match.ServerKey)
            throw new InvalidDataException("CoinRush network settings differ between source and cooked content.");
        LevelConfig? level = await LoadJson<LevelConfig>(ContentRefs.Game_CoinRush_Level, ct);
        if (level is not null) CoinRushContent.ValidateLevel(level);
        UiStrings? ui = await LoadJson<UiStrings>(ContentRefs.Game_CoinRush_UiStrings, ct);
        log.LogInformation("CoinRush client content ready (ui: {Title}).", ui?.Title ?? "fallback");

        if (world.GetPool<LocalPlayer>().Count > 0)
        {
            var locals = world.Where(out LocalAspect aspect);
            for (int i = 0; i < locals.Count; i++)
            {
                int entity = locals[i];
                if (!world.GetPool<ClientSessionState>().Has(entity))
                {
                    world.GetPool<ClientSessionState>().Add(entity) = new ClientSessionState
                    {
                        LastConnectAttempt = -1000,
                        PreviousTick = -1,
                        LatestTick = -1,
                        PendingLocalSlot = -1,
                    };
                }
            }
            log.LogInformation("Client display state survived hot reload.");
            return;
        }

        int lp = world.NewEntity();
        world.GetPool<LocalPlayer>().Add(lp) = new LocalPlayer { Slot = -1 };
        world.GetPool<ClientSessionState>().Add(lp) = new ClientSessionState
        {
            LastConnectAttempt = -1000,
            PreviousTick = -1,
            LatestTick = -1,
            PendingLocalSlot = -1,
        };

        SpawnLevelVisuals(level);
    }

    /// <summary>Creates visual geometry from loaded level data or the built-in fallback.</summary>
    /// <param name="level">Loaded level settings, if available.</param>
    private void SpawnLevelVisuals(LevelConfig? level)
    {
        if (level?.Platforms != null)
        {
            foreach (LevelPlatformDto p in level.Platforms)
            {
                SpawnPlatform(p.X, p.Y, p.Width, p.Height, p.IsMover);
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
            SpawnPlatform(p.X, p.Y, p.Width, p.Height, p.IsMover == 1);
        }
        foreach (BoxDesc b in LevelData.Crates)
        {
            SpawnCrate(b.X, b.Y, b.Size);
        }
        foreach (HazardDesc s in LevelData.Spikes)
        {
            SpawnSpike(s.X, s.Y, s.Width, s.Height);
        }
        foreach (CheckpointDesc c in LevelData.Checkpoints)
        {
            SpawnCheckpoint(c.X, c.Y, c.Radius);
        }
    }

    /// <summary>Creates a platform display entity.</summary>
    /// <param name="x">Horizontal center.</param>
    /// <param name="y">Vertical center.</param>
    /// <param name="w">Platform width.</param>
    /// <param name="h">Platform height.</param>
    /// <param name="mover">Whether to attach a moving-platform marker.</param>
    private void SpawnPlatform(float x, float y, float w, float h, bool mover)
    {
        int e = world.NewEntity();
        world.GetPool<PlatformTag>().Add(e) = new PlatformTag { Width = w, Height = h };
        world.GetPool<Karpik.Engine.Shared.Spatial2D.Transform2D>().Add(e) = new Karpik.Engine.Shared.Spatial2D.Transform2D
        {
            Position = new OpenTK.Mathematics.Vector2d(x, y),
            Rotation = 0f,
            Scale = new OpenTK.Mathematics.Vector2(1f, 1f),
        };
        if (mover)
        {
            world.GetPool<MovingPlatform>().Add(e) = new MovingPlatform
            {
                FromX = x, FromY = y, ToX = x, ToY = y, Speed = 0f,
                Phase = 0f, Direction = 1, Width = w, Height = h,
            };
        }
    }

    /// <summary>Creates a crate display entity.</summary>
    /// <param name="x">Horizontal center.</param>
    /// <param name="y">Vertical center.</param>
    /// <param name="size">Length of each side.</param>
    private void SpawnCrate(float x, float y, float size)
    {
        int e = world.NewEntity();
        world.GetPool<Crate>().Add(e) = new Crate { Width = size, Height = size };
        world.GetPool<Karpik.Engine.Shared.Spatial2D.Transform2D>().Add(e) = new Karpik.Engine.Shared.Spatial2D.Transform2D
        {
            Position = new OpenTK.Mathematics.Vector2d(x, y),
            Rotation = 0f,
            Scale = new OpenTK.Mathematics.Vector2(1f, 1f),
        };
    }

    /// <summary>Creates a spike display entity.</summary>
    /// <param name="x">Horizontal center.</param>
    /// <param name="y">Vertical center.</param>
    /// <param name="w">Spike width.</param>
    /// <param name="h">Spike height.</param>
    private void SpawnSpike(float x, float y, float w, float h)
    {
        int e = world.NewEntity();
        world.GetPool<Spike>().Add(e) = new Spike { Width = w, Height = h };
        world.GetPool<Karpik.Engine.Shared.Spatial2D.Transform2D>().Add(e) = new Karpik.Engine.Shared.Spatial2D.Transform2D
        {
            Position = new OpenTK.Mathematics.Vector2d(x, y),
            Rotation = 0f,
            Scale = new OpenTK.Mathematics.Vector2(1f, 1f),
        };
    }

    /// <summary>Creates a checkpoint display entity.</summary>
    /// <param name="x">Horizontal center.</param>
    /// <param name="y">Vertical center.</param>
    /// <param name="radius">Checkpoint trigger radius.</param>
    private void SpawnCheckpoint(float x, float y, float radius)
    {
        int e = world.NewEntity();
        world.GetPool<Checkpoint>().Add(e) = new Checkpoint { ZoneIndex = 0, X = x, Y = y, Radius = radius };
    }

    /// <summary>Loads and deserializes a cooked JSON asset, returning a fallback signal on failure.</summary>
    /// <typeparam name="T">Reference type represented by the JSON payload.</typeparam>
    /// <param name="asset">Cooked JSON asset reference.</param>
    /// <param name="ct">Cancellation token for loading.</param>
    /// <returns>The parsed value, or <see langword="null"/> if loading fails.</returns>
    private async ValueTask<T?> LoadJson<T>(AssetRef<RawJsonPayload> asset, CancellationToken ct) where T : class
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
            log.LogWarning(ex, "Client content load failed, code fallback in use.");
        }
        return null;
    }

    /// <summary>Selects the local client entity.</summary>
    private sealed class LocalAspect : EcsAspect
    {
        public EcsPool<LocalPlayer> Locals = Inc;
    }
}
