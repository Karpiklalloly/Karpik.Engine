using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Content.Runtime;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Draws the level, players, coins, and HUD into the engine command buffer.</summary>
/// <param name="world">Client world containing display entities.</param>
/// <param name="cameras">Camera used to position player labels.</param>
/// <param name="window">Viewport size for screen-space HUD placement.</param>
/// <param name="presentation">Scoped presentation owner and live Content bindings.</param>
/// <param name="time">Clock used for presentation animation.</param>
[SequentialSystem]
public sealed class ClientDrawSystem(
    EcsDefaultWorld world,
    GraphicsCameraState cameras,
    IWindow window,
    ClientPresentationAssets presentation,
    Time time)
    : ISystemAsyncInit, ISystemRenderPrepare, ISystemAsyncDestroy
{
    /// <summary>Warms queries and initializes presentation bindings/font on the cold startup path.</summary>
    public async ValueTask InitAsync(CancellationToken ct)
    {
        // At most two player labels plus phase and score use copied text buffers.
        GraphicsContext.EnsureThreadBufferCapacity(rects: 512, textures: 128, texts: 32,
            textChars: ClientHudText.MaximumFormattedLength * 4);
        world.Where(out GeoAspect _);
        world.Where(out CheckpointAspect _);
        world.Where(out CoinAspect _);
        world.Where(out PlayerAspect _);
        world.Where(out LocalAspect _);
        world.Where(out MatchAspect _);
        world.Where(out FxAspect _);
        var states = world.Where(out DrawStateAspect stateAspect);
        int stateEntity = states.Count == 0 ? world.NewEntity() : states[0];
        ClientDrawState handles = await presentation.InitializeAsync(ct);
        world.GetPool<ClientDrawState>().TryAddOrGet(stateEntity) = handles;
    }

    /// <summary>Drains the scoped presentation owner before gameplay teardown completes.</summary>
    public async ValueTask DestroyAsync()
    {
        await presentation.DisposeAsync();
        var states = world.Where(out DrawStateAspect aspect);
        if (states.Count != 0) aspect.States.Get(states[0]) = default;
    }

    /// <summary>Borrows current ready values for this frame and records draw commands.</summary>
    public void RenderPrepare()
    {
        var states = world.Where(out DrawStateAspect aspect);
        if (states.Count == 0) return;
        ref readonly ClientDrawState handles = ref aspect.States.Read(states[0]);
        ClientPresentationFrame state = presentation.Read(in handles);
        ICommandBuffer buf = GraphicsContext.Buffer;
        float now = (float)time.TotalTime;

        DrawBackdrop(buf);
        DrawLevel(buf, now, in state);
        DrawCoins(buf, now, in state);
        DrawPlayers(buf, now, in state);
        DrawHud(buf, in state);
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
    private void DrawLevel(ICommandBuffer buf, float now, in ClientPresentationFrame state)
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
    private void DrawCoins(ICommandBuffer buf, float now, in ClientPresentationFrame state)
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
    private void DrawPlayers(ICommandBuffer buf, float now, in ClientPresentationFrame state)
    {
        int localSlot = -1;
        var locals = world.Where(out LocalAspect localAspect);
        for (int i = 0; i < locals.Count; i++)
        {
            localSlot = localAspect.Locals.Read(locals[i]).Slot;
        }
        var players = world.Where(out PlayerAspect playerAspect);
        Span<char> label = stackalloc char[ClientHudText.MaximumFormattedLength];
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
                if (!ClientHudText.TryPlayerLabel(label, name, p.Score, out int labelLength))
                    throw new InvalidDataException("CoinRush player label exceeds the warmed HUD text capacity.");
                CommandBufferDrawExtensions.AddTextCenteredCopy(
                    buf, state.Font, label[..labelLength], screen,
                    18f, Color.White, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(5));
            }
        }
    }

    /// <summary>Draws match phase, score, controls, and end-of-match text.</summary>
    /// <param name="buf">Command buffer receiving screen-space HUD graphics.</param>
    /// <param name="state">Loaded font and localized strings.</param>
    private void DrawHud(ICommandBuffer buf, in ClientPresentationFrame state)
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
        Span<char> phase = stackalloc char[ClientHudText.MaximumFormattedLength];
        Span<char> score = stackalloc char[ClientHudText.MaximumFormattedLength];
        if (!ClientHudText.TryPhase(phase, state.Ui, in m, out int phaseLength) ||
            !ClientHudText.TryScore(score, state.Ui, in m, out int scoreLength))
            throw new InvalidDataException("CoinRush combined HUD text exceeds its warmed text capacity.");
        CommandBufferDrawExtensions.AddTextCentered(
            buf, state.Font, state.Ui.Title,
            new Vector2(cx, 16f), 18f, Color.White, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(11));
        CommandBufferDrawExtensions.AddTextCenteredCopy(
            buf, state.Font, phase[..phaseLength],
            new Vector2(cx, 40f), 18f, Color.White, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(11));
        CommandBufferDrawExtensions.AddTextCenteredCopy(
            buf, state.Font, score[..scoreLength],
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

/// <summary>Value-only handles for the scoped presentation owner; safe to retain in ECS.</summary>
public struct ClientDrawState : IEcsComponent
{
    public ContentBinding<UiStrings> Ui;
    public ContentBinding<ITexture2D> Player;
    public ContentBinding<ITexture2D> Coin;
    public ContentBinding<ITexture2D> Tile;
}
