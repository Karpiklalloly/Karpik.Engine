using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Content.Generated;
using Karpik.Content.Runtime;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Draws the level, players, coins, and HUD into the engine command buffer.</summary>
/// <param name="world">Client world containing display entities.</param>
/// <param name="cameras">Camera used to position player labels.</param>
/// <param name="window">Viewport size for screen-space HUD placement.</param>
/// <param name="assets">Asset manager loading textures and fonts.</param>
/// <param name="content">Registry loading localized UI strings.</param>
/// <param name="time">Clock used for presentation animation.</param>
/// <param name="log">Logger for asset loading diagnostics.</param>
[SequentialSystem]
public sealed class ClientDrawSystem(
    EcsDefaultWorld world,
    GraphicsCameraState cameras,
    IWindow window,
    IAssetsManager assets,
    IContentRegistry content,
    Time time,
    ILogger<ClientDrawSystem> log)
    : ISystemAsyncInit, ISystemRenderPrepare, ISystemDestroy
{
    /// <summary>Warms queries, loads UI strings, and starts graphic asset loads.</summary>
    /// <param name="ct">Cancellation token for UI content loading.</param>
    public async ValueTask InitAsync(CancellationToken ct)
    {
        world.Where(out GeoAspect _);
        world.Where(out CheckpointAspect _);
        world.Where(out CoinAspect _);
        world.Where(out PlayerAspect _);
        world.Where(out LocalAspect _);
        world.Where(out MatchAspect _);
        world.Where(out FxAspect _);
        var states = world.Where(out DrawStateAspect stateAspect);
        int stateEntity = states.Count == 0 ? world.NewEntity() : states[0];
        UiStrings? ui = await LoadJson<UiStrings>(ContentRefs.Game_CoinRush_UiStrings, ct);
        ref ClientDrawState state = ref world.GetPool<ClientDrawState>().TryAddOrGet(stateEntity);
        state.Ui = ui ?? CoinRushContent.DefaultUi();
        state.PlayerJob = assets.LoadAssetByPathAsync(ContentRefs.Game_Sprites_Player_SourcePath);
        state.CoinJob = assets.LoadAssetByPathAsync(ContentRefs.Game_CoinRush_Coin_SourcePath);
        state.TileJob = assets.LoadAssetByPathAsync(ContentRefs.Game_CoinRush_Tile_SourcePath);
        state.FontJob = assets.LoadAssetByPathAsync(ContentRefs.Game_PressStart_Font_SourcePath);
        log.LogInformation("CoinRush client draw init: texture/font loads started.");
    }

    /// <summary>Releases all loaded and pending graphic asset handles.</summary>
    public void Destroy()
    {
        var states = world.Where(out DrawStateAspect aspect);
        if (states.Count == 0) return;
        ref ClientDrawState state = ref aspect.States.Get(states[0]);
        state.PlayerHandle?.Dispose();
        state.CoinHandle?.Dispose();
        state.TileHandle?.Dispose();
        state.FontHandle?.Dispose();
        state.PlayerJob?.Dispose();
        state.CoinJob?.Dispose();
        state.TileJob?.Dispose();
        state.FontJob?.Dispose();
        state = default;
    }

    /// <summary>Completes ready loads and records this frame's draw commands.</summary>
    public void RenderPrepare()
    {
        var states = world.Where(out DrawStateAspect aspect);
        if (states.Count == 0) return;
        ref ClientDrawState state = ref aspect.States.Get(states[0]);
        PollAssets(ref state);
        ICommandBuffer buf = GraphicsContext.Buffer;
        float now = (float)time.TotalTime;

        DrawBackdrop(buf);
        DrawLevel(buf, now, in state);
        DrawCoins(buf, now, in state);
        DrawPlayers(buf, now, in state);
        DrawHud(buf, in state);
    }

    /// <summary>Transfers completed asset jobs into owned textures and fonts.</summary>
    /// <param name="state">Draw resources being updated.</param>
    private void PollAssets(ref ClientDrawState state)
    {
        if (state.PlayerTex is null && TryComplete(ref state.PlayerJob, out AssetHandle<Asset> h))
        {
            state.PlayerHandle = h;
            state.PlayerTex = (h.Asset as TextureAsset)?.Texture;
        }
        if (state.CoinTex is null && TryComplete(ref state.CoinJob, out AssetHandle<Asset> h2))
        {
            state.CoinHandle = h2;
            state.CoinTex = (h2.Asset as TextureAsset)?.Texture;
        }
        if (state.TileTex is null && TryComplete(ref state.TileJob, out AssetHandle<Asset> h3))
        {
            state.TileHandle = h3;
            state.TileTex = (h3.Asset as TextureAsset)?.Texture;
        }
        if (state.Font is null && TryComplete(ref state.FontJob, out AssetHandle<Asset> h4))
        {
            state.FontHandle = h4;
            state.Font = (h4.Asset as FontAsset)?.Font;
            if (state.Font is not null && !state.LoggedTextMode)
            {
                state.LoggedTextMode = true;
                log.LogInformation("Font ready, HUD text enabled.");
            }
        }
    }

    /// <summary>Reads and releases a completed asset job without blocking.</summary>
    /// <param name="job">Pending job, cleared after completion or failure.</param>
    /// <param name="handle">Completed asset handle when successful.</param>
    /// <returns><see langword="true"/> when the job completed with a valid asset.</returns>
    private static bool TryComplete(ref JobHandle<AssetHandle<Asset>>? job, out AssetHandle<Asset> handle)
    {
        handle = default;
        if (job is not { IsCompleted: true } j)
        {
            return false;
        }
        try
        {
            handle = j.GetAwaiter().GetResult();
        }
        catch
        {
            job = null;
            return false;
        }
        job = null;
        return handle.IsValid;
    }

    /// <summary>Draws the world-space background.</summary>
    /// <param name="buf">Command buffer receiving the backdrop.</param>
    private void DrawBackdrop(ICommandBuffer buf)
    {
        CommandBufferDrawExtensions.AddRectCentered(
            buf, new Vector2(0f, 2f), new Vector2(90f, 55f),
            Color.FromArgb(18, 22, 38), 0f, DrawSpace.World, DrawSortKey.FromLayer(0));
    }

    /// <summary>Draws level geometry and pulsing checkpoint markers.</summary>
    /// <param name="buf">Command buffer receiving level geometry.</param>
    /// <param name="now">Current presentation time in seconds.</param>
    /// <param name="state">Loaded draw resources.</param>
    private void DrawLevel(ICommandBuffer buf, float now, in ClientDrawState state)
    {
        var geo = world.Where(out GeoAspect aspect);
        var platformPool = world.GetPool<PlatformTag>();
        var cratePool = world.GetPool<Crate>();
        var spikePool = world.GetPool<Spike>();
        for (int i = 0; i < geo.Count; i++)
        {
            int e = geo[i];
            var t = aspect.Transforms.Read(e);
            var center = new Vector2((float)t.Position.X, (float)t.Position.Y);
            if (platformPool.Has(e))
            {
                PlatformTag p = platformPool.Read(e);
                var size = new Vector2(p.Width, p.Height);
                Color c = Color.FromArgb(96, 110, 150);
                if (state.TileTex is not null)
                {
                    CommandBufferDrawExtensions.AddTextureCentered(
                        buf, state.TileTex, center, size, Color.White, 0f,
                        DrawSpace.World, new Vector4(0, 1, 1, 0), DrawSortKey.FromLayer(1));
                }
                else
                {
                    CommandBufferDrawExtensions.AddRectCentered(
                        buf, center, size, c, 0f, DrawSpace.World, DrawSortKey.FromLayer(1));
                }
            }
            else if (cratePool.Has(e))
            {
                Crate c = cratePool.Read(e);
                CommandBufferDrawExtensions.AddRectCentered(
                    buf, center, new Vector2(c.Width, c.Height),
                    Color.FromArgb(150, 104, 58), t.Rotation, DrawSpace.World, DrawSortKey.FromLayer(2));
            }
            else if (spikePool.Has(e))
            {
                Spike s = spikePool.Read(e);
                CommandBufferDrawExtensions.AddRectCentered(
                    buf, center, new Vector2(s.Width, s.Height),
                    Color.FromArgb(220, 60, 60), 0f, DrawSpace.World, DrawSortKey.FromLayer(2));
            }
        }

        var checkpoints = world.Where(out CheckpointAspect checkpointAspect);
        for (int i = 0; i < checkpoints.Count; i++)
        {
            Checkpoint cp = checkpointAspect.Checkpoints.Read(checkpoints[i]);
            float pulse = 0.85f + (0.15f * MathF.Sin(now * 3f));
            float r = cp.Radius * 2f * pulse;
            CommandBufferDrawExtensions.AddRectCentered(
                buf, new Vector2(cp.X, cp.Y), new Vector2(r, r * 0.4f),
                Color.FromArgb(90, 80, 220, 120), 0f, DrawSpace.World, DrawSortKey.FromLayer(1));
        }
    }

    /// <summary>Draws active coins with a vertical bob animation.</summary>
    /// <param name="buf">Command buffer receiving coin graphics.</param>
    /// <param name="now">Current presentation time in seconds.</param>
    /// <param name="state">Loaded draw resources.</param>
    private void DrawCoins(ICommandBuffer buf, float now, in ClientDrawState state)
    {
        var coins = world.Where(out CoinAspect coinAspect);
        for (int i = 0; i < coins.Count; i++)
        {
            Coin c = coinAspect.Coins.Read(coins[i]);
            if (c.Active == 0)
            {
                continue;
            }
            float bob = MathF.Sin(now * 4f + (c.SpawnIndex * 0.7f)) * 0.12f;
            var center = new Vector2(c.X, c.Y + bob);
            var size = new Vector2(0.9f, 0.9f);
            if (state.CoinTex is not null)
            {
                CommandBufferDrawExtensions.AddTextureCentered(
                    buf, state.CoinTex, center, size, Color.White, 0f,
                    DrawSpace.World, new Vector4(0, 0, 1, 1), DrawSortKey.FromLayer(3));
            }
            else
            {
                CommandBufferDrawExtensions.AddRectCentered(
                    buf, center, size, Color.FromArgb(240, 200, 60), 0f,
                    DrawSpace.World, DrawSortKey.FromLayer(3));
            }
        }
    }

    /// <summary>Draws players, local-player markers, and player labels.</summary>
    /// <param name="buf">Command buffer receiving player graphics.</param>
    /// <param name="now">Current presentation time in seconds.</param>
    /// <param name="state">Loaded draw resources and labels.</param>
    private void DrawPlayers(ICommandBuffer buf, float now, in ClientDrawState state)
    {
        int localSlot = -1;
        var locals = world.Where(out LocalAspect localAspect);
        for (int i = 0; i < locals.Count; i++)
        {
            localSlot = localAspect.Locals.Read(locals[i]).Slot;
        }
        var players = world.Where(out PlayerAspect playerAspect);
        for (int i = 0; i < players.Count; i++)
        {
            int e = players[i];
            Player p = playerAspect.Players.Read(e);
            DisplayState d = playerAspect.Displays.Read(e);
            Color tint = Color.FromArgb(p.R, p.G, p.B);
            float pulse = d.ScalePulse;
            var center = new Vector2(d.X, d.Y + 0.2f);
            var size = new Vector2(0.9f * pulse, 1.7f * pulse);
            if (state.PlayerTex is not null)
            {
                CommandBufferDrawExtensions.AddTextureCentered(
                    buf, state.PlayerTex, center, size, tint, 0f,
                    DrawSpace.World, new Vector4(0f, 1f, 1f, 0f), DrawSortKey.FromLayer(4));
            }
            else
            {
                CommandBufferDrawExtensions.AddRectCentered(
                    buf, center, size, tint, 0f, DrawSpace.World, DrawSortKey.FromLayer(4));
            }
            if (p.Index == localSlot)
            {
                float w = 1.3f + (0.1f * MathF.Sin(now * 5f));
                CommandBufferDrawExtensions.AddRectCentered(
                    buf, new Vector2(d.X, d.Y - 1f), new Vector2(w, 0.15f),
                    Color.White, 0f, DrawSpace.World, DrawSortKey.FromLayer(4));
            }
            if (state.Font is not null)
            {
                Vector2 screen = cameras.ActiveCamera
                    .WithViewport(window.Width, window.Height)
                    .WorldToScreen(new Vector2(d.X, d.Y + 1.4f));
                string name = p.Index == 0 ? state.Ui.Player0 : state.Ui.Player1;
                CommandBufferDrawExtensions.AddTextCentered(
                    buf, state.Font, name + " " + p.Score, screen,
                    18f, Color.White, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(5));
            }
        }
    }

    /// <summary>Draws match phase, score, controls, and end-of-match text.</summary>
    /// <param name="buf">Command buffer receiving screen-space HUD graphics.</param>
    /// <param name="state">Loaded font and localized strings.</param>
    private void DrawHud(ICommandBuffer buf, in ClientDrawState state)
    {
        MatchState m = default;
        bool found = false;
        var matches = world.Where(out MatchAspect matchAspect);
        for (int i = 0; i < matches.Count; i++)
        {
            m = matchAspect.States.Read(matches[i]);
            found = true;
        }
        if (!found)
        {
            return;
        }
        Vector2 vp = new(window.Width, window.Height);
        if (state.Font is null)
        {
            return;
        }
        float cx = vp.X / 2f;
        CommandBufferDrawExtensions.AddRectCentered(
            buf, new Vector2(cx, 46f), new Vector2(vp.X, 92f),
            Color.FromArgb(140, 10, 14, 24), 0f, DrawSpace.Screen, DrawSortKey.FromLayer(10));
        string phase = m.Phase switch
        {
            MatchPhase.WaitingForPlayers => state.Ui.Waiting,
            MatchPhase.Countdown => state.Ui.Countdown + " " + MathF.Ceiling(m.TimeLeft),
            MatchPhase.Running => "⏱ " + MathF.Ceiling(m.TimeLeft),
            MatchPhase.Finished => m.Winner < 0 ? state.Ui.Draw : ((state.Ui.Title + " ") + (m.Winner == 0 ? state.Ui.Player0 : state.Ui.Player1)),
            _ => string.Empty,
        };
        string score = state.Ui.Player0 + " " + m.Score0 + " : " + m.Score1 + " " + state.Ui.Player1;
        CommandBufferDrawExtensions.AddTextCentered(
            buf, state.Font, state.Ui.Title,
            new Vector2(cx, 16f), 18f, Color.White, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(11));
        CommandBufferDrawExtensions.AddTextCentered(
            buf, state.Font, phase,
            new Vector2(cx, 40f), 18f, Color.White, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(11));
        CommandBufferDrawExtensions.AddTextCentered(
            buf, state.Font, score,
            new Vector2(cx, 66f), 18f, Color.White, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(11));
        CommandBufferDrawExtensions.AddTextCentered(
            buf, state.Font, state.Ui.ControlsHint,
            new Vector2(cx, vp.Y - 52f), 20f, Color.LightGray, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(11));
        CommandBufferDrawExtensions.AddTextCentered(
            buf, state.Font, m.Phase == MatchPhase.Finished ? state.Ui.NewRoundHint : state.Ui.RestartHint,
            new Vector2(cx, vp.Y - 26f), 20f, Color.LightGray, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(11));

        var banners = world.Where(out FxAspect fxAspect);
        for (int i = 0; i < banners.Count; i++)
        {
            FxEvent fx = fxAspect.Events.Read(banners[i]);
            if (fx.Kind != FxKind.MatchEnd)
            {
                continue;
            }
            CommandBufferDrawExtensions.AddTextCentered(
                buf, state.Font, state.Ui.Finished, new Vector2(cx, 120f),
                64f, Color.Gold, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(12));
        }
    }

    /// <summary>Loads and deserializes localized UI strings from cooked JSON content.</summary>
    /// <typeparam name="T">Reference type represented by the JSON asset.</typeparam>
    /// <param name="asset">Cooked JSON asset reference.</param>
    /// <param name="ct">Cancellation token for loading.</param>
    /// <returns>The parsed value, or <see langword="null"/> on failure.</returns>
    private async ValueTask<T?> LoadJson<T>(AssetRef<RawJsonPayload> asset, CancellationToken ct) where T : class
    {
        await Task.Yield();
        try
        {
            await content.LoadAsync(asset, ct);
            if (content.TryGet(asset, out AssetLease<RawJsonPayload> lease))
            {
                using (lease)
                {
                    string? json = lease.Payload?.Json;
                    if (!string.IsNullOrEmpty(json))
                    {
                        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Draw content load failed.");
        }
        return null;
    }

    /// <summary>Selects static geometry with world transforms.</summary>
    private sealed class GeoAspect : EcsAspect
    {
        public EcsPool<Karpik.Engine.Shared.Spatial2D.Transform2D> Transforms = Inc;
    }

    /// <summary>Selects checkpoint markers.</summary>
    private sealed class CheckpointAspect : EcsAspect
    {
        public EcsPool<Checkpoint> Checkpoints = Inc;
    }

    /// <summary>Selects coins to draw.</summary>
    private sealed class CoinAspect : EcsAspect
    {
        public EcsPool<Coin> Coins = Inc;
    }

    /// <summary>Selects players with display positions.</summary>
    private sealed class PlayerAspect : EcsAspect
    {
        public EcsPool<Player> Players = Inc;
        public EcsPool<DisplayState> Displays = Inc;
    }

    /// <summary>Selects the local player slot.</summary>
    private sealed class LocalAspect : EcsAspect
    {
        public EcsPool<LocalPlayer> Locals = Inc;
    }

    /// <summary>Selects current match state for the HUD.</summary>
    private sealed class MatchAspect : EcsAspect
    {
        public EcsPool<MatchState> States = Inc;
    }

    /// <summary>Selects active presentation effects for the HUD.</summary>
    private sealed class FxAspect : EcsAspect
    {
        public EcsPool<FxEvent> Events = Inc;
    }

    /// <summary>Selects loaded and pending draw assets.</summary>
    private sealed class DrawStateAspect : EcsAspect
    {
        public EcsPool<ClientDrawState> States = Inc;
    }
}

/// <summary>Owns client draw assets, loading jobs, and localized strings.</summary>
public struct ClientDrawState : IEcsComponent
{
    public ITexture2D? PlayerTex;
    public ITexture2D? CoinTex;
    public ITexture2D? TileTex;
    public IFont? Font;
    public AssetHandle<Asset>? PlayerHandle;
    public AssetHandle<Asset>? CoinHandle;
    public AssetHandle<Asset>? TileHandle;
    public AssetHandle<Asset>? FontHandle;
    public JobHandle<AssetHandle<Asset>>? PlayerJob;
    public JobHandle<AssetHandle<Asset>>? CoinJob;
    public JobHandle<AssetHandle<Asset>>? TileJob;
    public JobHandle<AssetHandle<Asset>>? FontJob;
    public UiStrings Ui;
    public bool LoggedTextMode;
}
