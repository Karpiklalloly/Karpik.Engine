using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Engine.Shared.Spatial2D;
using OpenTK.Mathematics;
using SSSuperGame.Shared.CoinRush;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks gameplay component flows against an ECS world and physics stub.</summary>
public sealed class EcsIntegrationTests
{
    /// <summary>Provides scripted physics results without running a physics simulation.</summary>
    private sealed class StubPhysics : IPhysicsWorld2D
    {
        public readonly Dictionary<int, Vector2d> Velocities = new();
        public readonly List<(Vector2d Start, Vector2d End)> Rays = new();
        public int GroundedHits;
        public CollisionEvent[] FrameContacts = Array.Empty<CollisionEvent>();
        private int _nextHandle = 1;

        /// <summary>Creates a synthetic handle for a physics body.</summary>
        /// <param name="entityId">The ECS entity ID.</param>
        /// <param name="position">The body position.</param>
        /// <param name="rotation">The body rotation.</param>
        /// <param name="bodyConfig">The body configuration.</param>
        /// <param name="shapeConfig">The body shape configuration.</param>
        /// <returns>A synthetic body handle.</returns>
        public PhysicsBodyHandle CreateBody(int entityId, Vector2d position, float rotation, in BodyConfig bodyConfig, in ShapeConfig shapeConfig)
        {
            return new PhysicsBodyHandle(_nextHandle++);
        }

        /// <summary>Accepts a physics step without simulating motion.</summary>
        /// <param name="dt">The fixed-step duration in seconds.</param>
        public void Step(double dt)
        {
        }

        /// <summary>Leaves batched body transforms unchanged in the test stub.</summary>
        /// <param name="handles">The body handles for the batch.</param>
        /// <param name="positions">The body positions.</param>
        /// <param name="rotations">The body rotations.</param>
        public void GetTransforms(ReadOnlySpan<PhysicsBodyHandle> handles, Span<Vector2d> positions, Span<float> rotations)
        {
        }

        /// <summary>Leaves batched body velocities unchanged in the test stub.</summary>
        /// <param name="handles">The body handles for the batch.</param>
        /// <param name="linear">The linear velocity or its output span.</param>
        /// <param name="angular">The angular velocity or its output span.</param>
        public void GetVelocities(ReadOnlySpan<PhysicsBodyHandle> handles, Span<Vector2d> linear, Span<double> angular)
        {
        }

        /// <summary>Ignores batched transform updates in the test stub.</summary>
        /// <param name="handles">The body handles for the batch.</param>
        /// <param name="positions">The body positions.</param>
        /// <param name="rotations">The body rotations.</param>
        public void SetTransforms(ReadOnlySpan<PhysicsBodyHandle> handles, ReadOnlySpan<Vector2d> positions, ReadOnlySpan<float> rotations)
        {
        }

        /// <summary>Ignores batched velocity updates in the test stub.</summary>
        /// <param name="handles">The body handles for the batch.</param>
        /// <param name="linear">The linear velocity or its output span.</param>
        /// <param name="angular">The angular velocity or its output span.</param>
        public void SetVelocities(ReadOnlySpan<PhysicsBodyHandle> handles, ReadOnlySpan<Vector2d> linear, ReadOnlySpan<double> angular)
        {
        }

        /// <summary>Ignores body destruction in the test stub.</summary>
        /// <param name="handle">The synthetic physics body handle.</param>
        public void DestroyBody(PhysicsBodyHandle handle)
        {
        }

        /// <summary>Reports a scripted ground hit when one is enabled.</summary>
        /// <param name="start">The ray start point.</param>
        /// <param name="end">The ray end point.</param>
        /// <param name="layerMask">The physics layers to query.</param>
        /// <param name="results">Receives physics query hits.</param>
        /// <returns>The number of scripted hits written to the result span.</returns>
        public int Raycast(Vector2d start, Vector2d end, PhysicsLayerMask layerMask, Span<RaycastHit2D> results)
        {
            Rays.Add((start, end));
            if (GroundedHits <= 0 || results.Length == 0)
            {
                return 0;
            }
            results[0] = new RaycastHit2D { Entity = -1, Fraction = 0.5f, Point = start, Normal = new Vector2d(0, 1) };
            return 1;
        }

        /// <summary>Reports no overlapping bodies in the test stub.</summary>
        /// <param name="center">The circle center.</param>
        /// <param name="radius">The circle radius.</param>
        /// <param name="layerMask">The physics layers to query.</param>
        /// <param name="results">Receives physics query hits.</param>
        /// <returns>Zero because the stub reports no overlaps.</returns>
        public int OverlapCircle(Vector2d center, double radius, PhysicsLayerMask layerMask, Span<RaycastHit2D> results) => 0;

        /// <summary>Returns scripted collisions for the current frame.</summary>
        /// <returns>The scripted collisions.</returns>
        public ReadOnlySpan<CollisionEvent> GetFrameCollisions() => FrameContacts;

        /// <summary>Ignores applied forces in the test stub.</summary>
        /// <param name="handle">The synthetic physics body handle.</param>
        /// <param name="force">The force vector.</param>
        /// <param name="point">The force application point.</param>
        public void ApplyForce(PhysicsBodyHandle handle, Vector2d force, Vector2d point)
        {
        }

        /// <summary>Ignores applied impulses in the test stub.</summary>
        /// <param name="handle">The synthetic physics body handle.</param>
        /// <param name="impulse">The impulse vector.</param>
        public void ApplyLinearImpulse(PhysicsBodyHandle handle, Vector2d impulse)
        {
        }

        /// <summary>Returns the unit mass used by the test stub.</summary>
        /// <param name="handle">The synthetic physics body handle.</param>
        /// <returns>The unit mass.</returns>
        public float GetMass(PhysicsBodyHandle handle) => 1f;

        /// <summary>Returns the velocity held by the test stub.</summary>
        /// <param name="handle">The synthetic physics body handle.</param>
        /// <returns>The current synthetic body velocity.</returns>
        public Vector2d GetVelocity(PhysicsBodyHandle handle)
        {
            return Velocities.TryGetValue(handle.Value, out Vector2d v) ? v : Vector2d.Zero;
        }

        /// <summary>Stores a body velocity in the test stub.</summary>
        /// <param name="handle">The synthetic physics body handle.</param>
        /// <param name="linear">The linear velocity or its output span.</param>
        public void SetVelocity(PhysicsBodyHandle handle, Vector2d linear)
        {
            Velocities[handle.Value] = linear;
        }
    }

    /// <summary>Creates an isolated ECS world for a test.</summary>
    /// <returns>A new ECS world.</returns>
    private static EcsDefaultWorld NewWorld() => new();

    /// <summary>Verifies coin pickup and respawn through shared gameplay components.</summary>
    [Fact]
    public void Player_collects_coin_via_shared_flow()
    {
        EcsDefaultWorld world = NewWorld();
        try
        {
            int player = world.NewEntity();
            world.GetPool<Player>().Add(player) = new Player { Index = 0, Score = 0 };
            world.GetPool<Transform2D>().Add(player) = new Transform2D { Position = new Vector2d(0, 0) };

            int coin = world.NewEntity();
            world.GetPool<Coin>().Add(coin) = new Coin { SpawnIndex = 0, X = 0.5f, Y = 0f, Active = 1 };

            var coinPool = world.GetPool<Coin>();
            var transformPool = world.GetPool<Transform2D>();
            var playerPool = world.GetPool<Player>();
            Transform2D t = transformPool.Read(player);
            ref Coin c = ref coinPool.Get(coin);
            float dx = (float)t.Position.X - c.X;
            float dy = (float)t.Position.Y - c.Y;
            if ((dx * dx) + (dy * dy) <= 1.1f * 1.1f)
            {
                c.Active = 0;
                c.RespawnIn = MatchRules.CoinRespawnDelay;
                ref Player p = ref playerPool.Get(player);
                p.Score++;
            }

            Assert.Equal(0, coinPool.Read(coin).Active);
            Assert.Equal(1, playerPool.Read(player).Score);

            ref Coin ticking = ref coinPool.Get(coin);
            ticking.RespawnIn = MatchRules.TickCoin(ticking.RespawnIn, MatchRules.CoinRespawnDelay);
            Assert.True(MatchRules.CoinReady(ticking.RespawnIn));
        }
        finally
        {
            world.Destroy();
        }
    }

    /// <summary>Verifies respawn at the most recent checkpoint after falling out.</summary>
    [Fact]
    public void Death_respawns_at_last_checkpoint()
    {
        EcsDefaultWorld world = NewWorld();
        try
        {
            int player = world.NewEntity();
            world.GetPool<Player>().Add(player) = new Player { Index = 1 };
            world.GetPool<RespawnAnchor>().Add(player) = new RespawnAnchor { X = 10f, Y = 2.6f };
            world.GetPool<Transform2D>().Add(player) = new Transform2D { Position = new Vector2d(10, -50) };
            world.GetPool<TeleportRequest>().Add(player) = default;

            Assert.True(MatchRules.FellOut(-50f, LevelData.KillY));

            RespawnAnchor a = world.GetPool<RespawnAnchor>().Read(player);
            var dest = new Vector2d(a.X, a.Y + 0.5);
            world.GetPool<TeleportRequest>().Get(player) = new TeleportRequest { Position = dest, Rotation = 0f };

            Assert.Equal(10.0, world.GetPool<TeleportRequest>().Read(player).Position.X, precision: 6);
            Assert.Equal(3.1, world.GetPool<TeleportRequest>().Read(player).Position.Y, precision: 4);
        }
        finally
        {
            world.Destroy();
        }
    }

    /// <summary>Verifies raycast results are written into the caller-provided span.</summary>
    [Fact]
    public void Ground_check_uses_caller_provided_span()
    {
        EcsDefaultWorld world = NewWorld();
        try
        {
            var physics = new StubPhysics { GroundedHits = 1 };
            var start = new Vector2d(0, -0.8);
            var end = new Vector2d(0, -1.05);
            Span<RaycastHit2D> hits = stackalloc RaycastHit2D[4];
            int n = physics.Raycast(start, end, new PhysicsLayerMask(CoinRushLayers.Platform | CoinRushLayers.Crate), hits);
            Assert.Equal(1, n);
            Assert.Single(physics.Rays);
        }
        finally
        {
            world.Destroy();
        }
    }

    /// <summary>Verifies jump velocity after leaving the ground within coyote time.</summary>
    [Fact]
    public void Coyote_jump_sets_server_velocity()
    {
        EcsDefaultWorld world = NewWorld();
        try
        {
            var physics = new StubPhysics();
            int player = world.NewEntity();
            world.GetPool<MoveState>().Add(player) = new MoveState();
            world.GetPool<PhysicsBodyRef>().Add(player) = new PhysicsBodyRef { Handle = new PhysicsBodyHandle(7) };

            ref MoveState mv = ref world.GetPool<MoveState>().Get(player);
            MatchRules.UpdateJump(ref mv, grounded: true, jumpPressed: false, dt: 1f / 60f);
            bool jump = MatchRules.UpdateJump(ref mv, grounded: false, jumpPressed: true, dt: 1f / 60f);
            Assert.True(jump);
            physics.SetVelocity(new PhysicsBodyHandle(7), new Vector2d(8, MatchRules.JumpVelocity()));
            Assert.Equal(MatchRules.JumpVelocity(), (float)physics.GetVelocity(new PhysicsBodyHandle(7)).Y, precision: 4);
        }
        finally
        {
            world.Destroy();
        }
    }

    /// <summary>Verifies that scripted frame collisions are exposed.</summary>
    [Fact]
    public void Frame_collisions_are_drained()
    {
        var physics = new StubPhysics
        {
            FrameContacts = new[] { new CollisionEvent { EntityA = 1, EntityB = 2 } },
        };
        Assert.Equal(1, physics.GetFrameCollisions().Length);
    }
}
