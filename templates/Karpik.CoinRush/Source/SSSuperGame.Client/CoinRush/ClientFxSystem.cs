using System.Buffers;
using DCFApixels.DragonECS;
using GTweens.Builders;
using GTweens.Extensions;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Tweening;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Animates local presentation effects produced by replicated match events.</summary>
/// <param name="world">Client display world containing effect and player components.</param>
/// <param name="tween">Tween scheduler for player pulses.</param>
/// <param name="time">Frame clock used to age effects.</param>
[SequentialSystem]
public sealed class ClientFxSystem(EcsDefaultWorld world, Tween tween, Time time) : ISystemUpdate
{
    /// <summary>Starts new effects, ages active effects, and removes expired entities.</summary>
    public void Update()
    {
        float dt = (float)time.DeltaTime;
        var events = world.Where(out FxAspect aspect);
        if (events.Count == 0)
        {
            DecayPulses(dt);
            return;
        }

        int[] doomed = ArrayPool<int>.Shared.Rent(events.Count);
        try
        {
            int n = 0;
            for (int i = 0; i < events.Count; i++)
            {
                int e = events[i];
                ref FxEvent fx = ref aspect.Events.Get(e);
                if (fx.Age == 0f) Ignite(fx);
                fx.Age += dt;
                if (fx.Age >= fx.Duration) doomed[n++] = e;
            }
            for (int i = 0; i < n; i++) world.DelEntity(doomed[i]);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(doomed);
        }
        DecayPulses(dt);
    }

    /// <summary>Starts a player pulse for the specified effect kind.</summary>
    /// <param name="fx">Effect event to display.</param>
    private void Ignite(FxEvent fx)
    {
        switch (fx.Kind)
        {
            case FxKind.CoinPickup:
                PulsePlayer(fx.PlayerIndex, 1.6f);
                break;
            case FxKind.Death:
                PulsePlayer(fx.PlayerIndex, 0.6f);
                break;
            case FxKind.Checkpoint:
                PulsePlayer(fx.PlayerIndex, 1.3f);
                break;
            case FxKind.MatchEnd:
                PulsePlayer(0, 1.4f);
                PulsePlayer(1, 1.4f);
                break;
        }
    }

    /// <summary>Sets a player's scale pulse and tweens it back to normal.</summary>
    /// <param name="index">Player slot to animate.</param>
    /// <param name="peak">Initial scale pulse.</param>
    private void PulsePlayer(int index, float peak)
    {
        var players = world.Where(out PlayerAspect aspect);
        for (int i = 0; i < players.Count; i++)
        {
            int e = players[i];
            if (aspect.Players.Read(e).Index != index)
            {
                continue;
            }
            int captured = e;
            ref DisplayState d = ref aspect.Displays.Get(captured);
            d.ScalePulse = peak;
            tween.Add(
                GTweenExtensions.Tween(
                    () => aspect.Displays.Read(captured).ScalePulse,
                    v => { aspect.Displays.Get(captured).ScalePulse = v; },
                    1f,
                    0.45f),
                pausable: false);
            ScheduleReset(captured);
        }
    }

    /// <summary>Schedules an exact scale reset after a pulse tween.</summary>
    /// <param name="entity">Player display entity to reset if it still exists.</param>
    private void ScheduleReset(int entity)
    {
        tween.Add(
            GTweenSequenceBuilder.New()
                .AppendTime(0.5f)
                .AppendCallback(() =>
                {
                    var displayPool = world.GetPool<DisplayState>();
                    if (displayPool.Has(entity))
                    {
                        displayPool.Get(entity).ScalePulse = 1f;
                    }
                })
                .Build(),
            pausable: false);
    }

    /// <summary>Clamps display pulses to the supported visual range.</summary>
    /// <param name="dt">Elapsed frame time.</param>
    private void DecayPulses(float dt)
    {
        var displays = world.Where(out DisplayAspect aspect);
        for (int i = 0; i < displays.Count; i++)
        {
            ref DisplayState d = ref aspect.Displays.Get(displays[i]);
            if (d.ScalePulse > 2f)
            {
                d.ScalePulse = 2f;
            }
            else if (d.ScalePulse < 0.5f)
            {
                d.ScalePulse = 0.5f;
            }
        }
    }

    /// <summary>Selects active effect entities.</summary>
    private sealed class FxAspect : EcsAspect
    {
        public EcsPool<FxEvent> Events = Inc;
    }

    /// <summary>Selects players with mutable display state.</summary>
    private sealed class PlayerAspect : EcsAspect
    {
        public EcsPool<Player> Players = Inc;
        public EcsPool<DisplayState> Displays = Inc;
    }

    /// <summary>Selects all player display states for pulse clamping.</summary>
    private sealed class DisplayAspect : EcsAspect
    {
        public EcsPool<DisplayState> Displays = Inc;
    }
}
