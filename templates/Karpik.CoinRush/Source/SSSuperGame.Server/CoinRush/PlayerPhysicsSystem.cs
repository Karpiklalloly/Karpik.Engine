using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Engine.Shared.Spatial2D;
using Microsoft.Extensions.Logging;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Server.CoinRush;

/// <summary>Applies player movement and moving-platform motion each fixed tick.</summary>
/// <param name="world">The authoritative entity world.</param>
/// <param name="physics">The physics world used for raycasts and body velocities.</param>
/// <param name="time">The engine fixed-step clock.</param>
/// <param name="log">The logger for physics failures.</param>
public sealed class PlayerPhysicsSystem(
    EcsDefaultWorld world,
    IPhysicsWorld2D physics,
    Time time,
    ILogger<PlayerPhysicsSystem> log)
    : ISystemFixedUpdate
{
    /// <summary>Consumes input latches and advances player and platform motion.</summary>
    public void FixedUpdate()
    {
        float dt = (float)time.FixedDeltaTime;
        StepMovers(dt);

        var latchPool = world.GetPool<InputLatch>();
        var bodyPool = world.GetPool<PhysicsBodyRef>();
        var netPool = world.GetPool<NetState>();

        var entities = world.Where(out PlayerAspect aspect);
        for (int i = 0; i < entities.Count; i++)
        {
            int e = entities[i];
            ref MoveState mv = ref aspect.Moves.Get(e);
            Transform2D t = aspect.Transforms.Read(e);
            float axis = 0f;
            bool jumpPressed = false;
            if (latchPool.Has(e))
            {
                ref InputLatch latch = ref latchPool.Get(e);
                axis = latch.MoveAxis;
                if (latch.Jump != 0)
                {
                    jumpPressed = true;
                    latch.Jump = 0;
                }
                if (latch.HasInput != 0)
                {
                    latch.HasInput = 0;
                    mv.LastInputSequence = latch.Sequence;
                }
            }
            mv.MoveAxis = axis;

            float px = (float)t.Position.X;
            float py = (float)t.Position.Y;

            bool grounded = GroundCheck(e, px, py);
            bool doJump = MatchRules.UpdateJump(ref mv, grounded, jumpPressed, dt);

            float targetX = MatchRules.TargetSpeed(axis, grounded);
            float velY;
            bool hasBody = bodyPool.Has(e) && bodyPool.Read(e).Handle.IsValid;
            OpenTK.Mathematics.Vector2d currentVel = hasBody
                ? physics.GetVelocity(bodyPool.Read(e).Handle)
                : new OpenTK.Mathematics.Vector2d(0, mv.FallbackVelY);

            if (doJump)
            {
                velY = MatchRules.JumpVelocity();
            }
            else if (hasBody)
            {
                velY = MatchRules.ClampFall((float)currentVel.Y);
            }
            else
            {
                float fall = (float)currentVel.Y + (MatchRules.Gravity * dt);
                velY = MatchRules.ClampFall(fall);
            }
            mv.FallbackVelY = velY;
            if (!hasBody)
            {
                t.Position = new OpenTK.Mathematics.Vector2d(px + (targetX * dt), py + (velY * dt));
                aspect.Transforms.Get(e) = t;
            }

            var linear = new OpenTK.Mathematics.Vector2d(targetX, velY);
            if (netPool.Has(e))
            {
                ref NetState ns = ref netPool.Get(e);
                Transform2D fresh = aspect.Transforms.Read(e);
                ns.X = (float)fresh.Position.X;
                ns.Y = (float)fresh.Position.Y;
            }
            if (!bodyPool.Has(e))
            {
                continue;
            }
            world.GetPool<SetVelocityRequest>().Add(e) = new SetVelocityRequest { Linear = linear, Angular = 0 };
            PhysicsBodyRef bref = bodyPool.Read(e);
            if (bref.Handle.IsValid)
            {
                try
                {
                    physics.SetVelocity(bref.Handle, linear);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "SetVelocity failed.");
                }
            }
            else
            {
                var matches = world.Where(out MatchAspect matchAspect);
                if (matches.Count > 0 && !matchAspect.States.Read(matches[0]).LoggedNoBodies)
                {
                    matchAspect.States.Get(matches[0]).LoggedNoBodies = true;
                    log.LogInformation("Physics bodies not created yet, using transform fallback.");
                }
            }

        }
    }

    /// <summary>Checks whether a player stands on another collidable entity.</summary>
    /// <param name="self">The player entity to exclude from raycast hits.</param>
    /// <param name="px">The player horizontal position.</param>
    /// <param name="py">The player vertical position.</param>
    /// <returns><see langword="true"/> when the ground ray hits another entity.</returns>
    private bool GroundCheck(int self, float px, float py)
    {
        try
        {
            var start = new OpenTK.Mathematics.Vector2d(px, py - 0.8);
            var end = new OpenTK.Mathematics.Vector2d(px, py - 0.8 - MatchRules.GroundRayLength);
            var mask = new PhysicsLayerMask(CoinRushLayers.Platform | CoinRushLayers.Crate);
            Span<RaycastHit2D> hits = stackalloc RaycastHit2D[4];
            int n = physics.Raycast(start, end, mask, hits);
            for (int i = 0; i < n; i++)
            {
                if (hits[i].Entity != self)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Raycast failed.");
        }
        return false;
    }

    /// <summary>Moves platforms and updates their physics bodies.</summary>
    /// <param name="dt">The fixed-step duration in seconds.</param>
    private void StepMovers(float dt)
    {
        var bodyPool = world.GetPool<PhysicsBodyRef>();
        var movers = world.Where(out MoverAspect aspect);
        for (int i = 0; i < movers.Count; i++)
        {
            int e = movers[i];
            ref MovingPlatform m = ref aspect.Movers.Get(e);
            MatchRules.StepMover(ref m, dt);
            MatchRules.MoverPosition(in m, out float x, out float y);
            Transform2D t = aspect.Transforms.Read(e);
            t.Position = new OpenTK.Mathematics.Vector2d(x, y);
            aspect.Transforms.Get(e) = t;
            world.GetPool<TeleportRequest>().Add(e) = new TeleportRequest
            {
                Position = new OpenTK.Mathematics.Vector2d(x, y),
                Rotation = 0f,
            };
            if (bodyPool.Has(e))
            {
                PhysicsBodyRef bref = bodyPool.Read(e);
                if (bref.Handle.IsValid)
                {
                    try
                    {
                        float vx = (m.ToX - m.FromX) * m.Direction * m.Speed;
                        float vy = (m.ToY - m.FromY) * m.Direction * m.Speed;
                        physics.SetVelocity(bref.Handle, new OpenTK.Mathematics.Vector2d(vx, vy));
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "Mover velocity failed.");
                    }
                }
            }
        }
    }

    /// <summary>Selects player movement components.</summary>
    private sealed class PlayerAspect : EcsAspect
    {
        public EcsPool<Player> Players = Inc;
        public EcsPool<MoveState> Moves = Inc;
        public EcsPool<Transform2D> Transforms = Inc;
    }

    /// <summary>Selects moving-platform components.</summary>
    private sealed class MoverAspect : EcsAspect
    {
        public EcsPool<MovingPlatform> Movers = Inc;
        public EcsPool<Transform2D> Transforms = Inc;
    }

    /// <summary>Selects the match state for physics diagnostics.</summary>
    private sealed class MatchAspect : EcsAspect
    {
        public EcsPool<MatchState> States = Inc;
    }
}
