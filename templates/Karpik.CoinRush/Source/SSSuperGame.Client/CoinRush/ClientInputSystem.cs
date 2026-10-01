using DCFApixels.DragonECS;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Modules.Window.Core;
using Karpik.Engine.Shared.Network.Core;
using Microsoft.Extensions.Logging;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Sends sequenced player commands after the server assigns a link and retries lost connections.</summary>
/// <param name="world">Client world containing link state.</param>
/// <param name="input">Keyboard input source.</param>
/// <param name="net">Network manager used to send commands and reconnect.</param>
/// <param name="config">Server address, port, and key.</param>
/// <param name="time">Clock used to throttle connection attempts.</param>
/// <param name="log">Logger for network failures.</param>
[SequentialSystem]
public sealed class ClientInputSystem(
    EcsDefaultWorld world,
    Input input,
    INetworkManager net,
    NetworkConfig config,
    Time time,
    ILogger<ClientInputSystem> log)
    : ISystemUpdate
{
    /// <summary>Reads controls and sends one command if a validated server link exists.</summary>
    public void Update()
    {
        var sessions = world.Where(out SessionAspect sessionAspect);
        if (sessions.Count == 0) return;
        ref ClientSessionState session = ref sessionAspect.Sessions.Get(sessions[0]);
        bool left = input.IsDown(NeoVeldrid.Key.A) || input.IsDown(NeoVeldrid.Key.Left);
        bool right = input.IsDown(NeoVeldrid.Key.D) || input.IsDown(NeoVeldrid.Key.Right);
        bool jump = input.IsPressed(NeoVeldrid.Key.Space) || input.IsPressed(NeoVeldrid.Key.W) || input.IsPressed(NeoVeldrid.Key.Up);
        bool restart = input.IsDown(NeoVeldrid.Key.R) || input.IsPressed(NeoVeldrid.Key.Enter) || input.IsPressed(NeoVeldrid.Key.KeypadEnter);

        float axis = 0f;
        if (left && !right)
        {
            axis = -1f;
        }
        else if (right && !left)
        {
            axis = 1f;
        }

        IPeer? peer = net.FirstPeer;
        if (peer is null || peer.ConnectionState != ConnectionState.Connected)
        {
            ClearLink();
            if (time.TotalTime - session.LastConnectAttempt > 5.0)
            {
                session.LastConnectAttempt = time.TotalTime;
                try
                {
                    log.LogInformation("Reconnecting to {Addr}:{Port}.", config.Address, config.Port);
                    net.Connect(config.Address, config.Port, config.Key);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Reconnect failed.");
                }
            }
            return;
        }

        if (!HasLink())
        {
            return;
        }

        var cmd = new InputCommand
        {
            Sequence = ++session.Sequence,
            MoveAxis = axis,
            Jump = jump,
            RestartRequest = restart,
        };
        try
        {
            IWriter w = net.CreateWriter();
            cmd.Write(w);
            peer.Send(w, InputCommand.Delivery);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Input send failed.");
        }
    }

    /// <summary>Checks whether a slot assignment or snapshot has established the server link.</summary>
    /// <returns><see langword="true"/> when input may be sent.</returns>
    private bool HasLink()
    {
        var locals = world.Where(out LinkLocalAspect aspect);
        for (int i = 0; i < locals.Count; i++)
        {
            if (aspect.Locals.Read(locals[i]).Slot >= 0)
            {
                return true;
            }
        }
        var matches = world.Where(out LinkMatchAspect matchAspect);
        for (int i = 0; i < matches.Count; i++)
        {
            if (matchAspect.States.Read(matches[i]).SnapTick >= 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Forgets local slot and snapshot state after a disconnection.</summary>
    private void ClearLink()
    {
        var locals = world.Where(out LinkLocalAspect aspect);
        for (int i = 0; i < locals.Count; i++)
        {
            ref LocalPlayer lp = ref aspect.Locals.Get(locals[i]);
            lp.Slot = -1;
        }
        var matches = world.Where(out LinkMatchAspect matchAspect);
        for (int i = 0; i < matches.Count; i++)
        {
            ref MatchState m = ref matchAspect.States.Get(matches[i]);
            m.SnapTick = -1;
            m.SnapshotAge = -1f;
        }
    }

    /// <summary>Selects the local slot component used by link checks.</summary>
    private sealed class LinkLocalAspect : EcsAspect
    {
        public EcsPool<LocalPlayer> Locals = Inc;
    }

    /// <summary>Selects match snapshots used by link checks.</summary>
    private sealed class LinkMatchAspect : EcsAspect
    {
        public EcsPool<MatchState> States = Inc;
    }

    /// <summary>Selects the client session state.</summary>
    private sealed class SessionAspect : EcsAspect
    {
        public EcsPool<ClientSessionState> Sessions = Inc;
    }
}
