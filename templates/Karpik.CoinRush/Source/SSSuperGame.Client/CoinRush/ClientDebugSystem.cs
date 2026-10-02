using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Modules.Window.Core;
using Karpik.Engine.Shared.Network.Core;
using Microsoft.Extensions.Logging;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Renders an optional diagnostic overlay and periodically logs client state.</summary>
/// <param name="world">World containing display and match components.</param>
/// <param name="cameras">Active camera state for diagnostic logging.</param>
/// <param name="overlay">Overlay visibility toggled by F1.</param>
/// <param name="input">Key source used for the overlay toggle.</param>
/// <param name="net">Network manager used to report connection state.</param>
/// <param name="metrics">Client frame timing and allocation metrics.</param>
/// <param name="time">Clock used to rate-limit logs.</param>
/// <param name="log">Logger for periodic diagnostics.</param>
[SequentialSystem]
public sealed class ClientDebugSystem(
    EcsDefaultWorld world,
    GraphicsCameraState cameras,
    ImGuiOverlayState overlay,
    IInputSource input,
    INetworkManager net,
    ClientFrameMetrics metrics,
    Time time,
    ILogger<ClientDebugSystem> log)
    : ISystemRenderPrepare
{
    /// <summary>Toggles and draws the overlay, then logs diagnostic state at intervals.</summary>
    public void RenderPrepare()
    {
        var sessions = world.Where(out SessionAspect sessionAspect);
        if (sessions.Count == 0) return;
        ref ClientSessionState session = ref sessionAspect.Sessions.Get(sessions[0]);
        bool f1 = false;
        foreach (NeoVeldrid.Key k in input.PressedKeys)
        {
            if (k == NeoVeldrid.Key.F1)
            {
                f1 = true;
            }
        }
        if (f1 && !session.WasF1)
        {
            overlay.Toggle();
        }
        session.WasF1 = f1;

        if (!overlay.Enabled)
        {
            return;
        }

        int players = 0;
        int coins = 0;
        int coinsActive = 0;
        int fx = 0;
        float p0x = 0f, p0y = 0f, p1x = 0f, p1y = 0f;
        var playerEntities = world.Where(out PlayerAspect playerAspect);
        players = playerEntities.Count;
        for (int i = 0; i < playerEntities.Count; i++)
        {
            int e = playerEntities[i];
            DisplayState d = playerAspect.Displays.Read(e);
            if (playerAspect.Players.Read(e).Index == 0)
            {
                p0x = d.X;
                p0y = d.Y;
            }
            else
            {
                p1x = d.X;
                p1y = d.Y;
            }
        }
        int localSlot = -1;
        var localEntities = world.Where(out LocalAspect localAspect);
        for (int i = 0; i < localEntities.Count; i++)
        {
            localSlot = localAspect.Locals.Read(localEntities[i]).Slot;
        }
        var coinEntities = world.Where(out CoinAspect coinAspect);
        coins = coinEntities.Count;
        for (int i = 0; i < coinEntities.Count; i++)
        {
            if (coinAspect.Coins.Read(coinEntities[i]).Active != 0)
            {
                coinsActive++;
            }
        }
        var fxEntities = world.Where(out FxAspect fxAspect);
        fx = fxEntities.Count;

        int tick = -1;
        float age = -1f;
        int s0 = 0, s1 = 0;
        byte phase = 0;
        var matchEntities = world.Where(out MatchAspect matchAspect);
        for (int i = 0; i < matchEntities.Count; i++)
        {
            MatchState m = matchAspect.States.Read(matchEntities[i]);
            tick = m.SnapTick;
            age = m.SnapshotAge;
            s0 = m.Score0;
            s1 = m.Score1;
            phase = (byte)m.Phase;
        }

        ClientFrameTimingSnapshot snap = metrics.GetSnapshot();
        string conn = net.FirstPeer?.ConnectionState.ToString() ?? "none";

        ICommandBuffer buf = GraphicsContext.Buffer;
        DrawBar(buf, 0, tick < 0 ? 0f : 1f, Color.Gray);
        DrawBar(buf, 1, age < 0f ? 0f : Math.Max(0f, 1f - (age * 2f)), age > 0.5f ? Color.Red : Color.Green);
        float alloc01 = snap.MergeAllocations.TotalBytes <= 0
            ? 0f
            : Math.Min(1f, snap.MergeAllocations.TotalBytes / 1048576f);
        DrawBar(buf, 2, alloc01, Color.CornflowerBlue);

        if (time.TotalTime - session.LastLog > 5.0)
        {
            session.LastLog = time.TotalTime;
            log.LogInformation(
                "DBG tick={Tick} age={Age:F2}s conn={Conn} phase={Phase} players={P} localSlot={Local} coins={C}/{T} fx={Fx} " +
                "p0=({X0:F1},{Y0:F1}) p1=({X1:F1},{Y1:F1}) camera=({CamX:F1},{CamY:F1}) score={S0}:{S1} mergeAlloc={Alloc}B gen0={Gen0}",
                tick, age, conn, phase, players, localSlot, coinsActive, coins, fx,
                p0x, p0y, p1x, p1y,
                cameras.ActiveCamera.Position.X, cameras.ActiveCamera.Position.Y,
                s0, s1,
                snap.MergeAllocations.TotalBytes, snap.MergeAllocations.Gen0CollectionCount);
        }
    }

    /// <summary>Draws one screen-space status bar.</summary>
    /// <param name="buf">Command buffer receiving the rectangles.</param>
    /// <param name="row">Zero-based bar row.</param>
    /// <param name="fill">Filled fraction, clamped to the unit interval.</param>
    /// <param name="color">Color of the filled portion.</param>
    private static void DrawBar(ICommandBuffer buf, int row, float fill, Color color)
    {
        if (fill < 0f)
        {
            fill = 0f;
        }
        else if (fill > 1f)
        {
            fill = 1f;
        }
        var bg = new Vector2(-620f, 300f - (row * 22f));
        CommandBufferDrawExtensions.AddRectCentered(
            buf, bg, new Vector2(120f, 14f),
            Color.FromArgb(120, 20, 20, 26), 0f, DrawSpace.Screen, DrawSortKey.FromLayer(20));
        CommandBufferDrawExtensions.AddRectCentered(
            buf, new Vector2(bg.X - (60f * (1f - fill)), bg.Y), new Vector2(120f * fill, 14f),
            color, 0f, DrawSpace.Screen, DrawSortKey.FromLayer(21));
    }

    /// <summary>Selects player identity and display state for diagnostics.</summary>
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

    /// <summary>Selects coins for active-count diagnostics.</summary>
    private sealed class CoinAspect : EcsAspect
    {
        public EcsPool<Coin> Coins = Inc;
    }

    /// <summary>Selects active client effects.</summary>
    private sealed class FxAspect : EcsAspect
    {
        public EcsPool<FxEvent> Events = Inc;
    }

    /// <summary>Selects the current match state.</summary>
    private sealed class MatchAspect : EcsAspect
    {
        public EcsPool<MatchState> States = Inc;
    }

    /// <summary>Selects client diagnostic state.</summary>
    private sealed class SessionAspect : EcsAspect
    {
        public EcsPool<ClientSessionState> Sessions = Inc;
    }
}
