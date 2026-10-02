using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Engine.Shared.Spatial2D;
using Microsoft.Extensions.Logging;
using OpenTK.Mathematics;
using SSSuperGame.Shared.CoinRush;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks fixed-tick movement through the real server physics system.</summary>
public sealed class PhysicsSmoothnessTests
{
    /// <summary>Provides scripted physics results without running a physics simulation.</summary>
    internal sealed class StubPhysics : IPhysicsWorld2D
    {
        public int GroundedHits = 999;
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

        /// <summary>Returns zero velocity from the stateless physics stub.</summary>
        /// <param name="handle">The synthetic physics body handle.</param>
        /// <returns>Zero velocity.</returns>
        public Vector2d GetVelocity(PhysicsBodyHandle handle) => Vector2d.Zero;

        /// <summary>Ignores velocity updates in the stateless physics stub.</summary>
        /// <param name="handle">The synthetic physics body handle.</param>
        /// <param name="linear">The linear velocity or its output span.</param>
        public void SetVelocity(PhysicsBodyHandle handle, Vector2d linear)
        {
        }
    }

    /// <summary>Discards engine logs during physics tests.</summary>
    /// <typeparam name="T">The logger category.</typeparam>
    private sealed class TestLogger<T> : ILogger<T>
    {
        /// <summary>Returns no scope for the test logger.</summary>
        /// <typeparam name="TState">The scope state type.</typeparam>
        /// <param name="state">The scope state.</param>
        /// <returns>No scope.</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>Indicates whether the test logger accepts messages.</summary>
        /// <param name="logLevel">The severity of the log entry.</param>
        /// <returns>Whether logging is enabled.</returns>
        public bool IsEnabled(LogLevel logLevel) => false;
        /// <summary>Discards a formatted test log entry.</summary>
        /// <typeparam name="TState">The log state type.</typeparam>
        /// <param name="logLevel">The severity of the log entry.</param>
        /// <param name="eventId">The event identifier.</param>
        /// <param name="state">The log state or scope state.</param>
        /// <param name="exception">The optional associated exception.</param>
        /// <param name="formatter">Formats the log state and exception.</param>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    /// <summary>Creates engine time with a specified fixed-step duration.</summary>
    /// <param name="fixedDt">The fixed-step duration in seconds.</param>
    /// <returns>Engine time configured for the test.</returns>
    private static Time NewTime(double fixedDt)
    {
        var time = new Time();
        MethodInfo? setter = typeof(Time).GetProperty("FixedDeltaTime")?.GetSetMethod(true);
        Assert.NotNull(setter);
        setter!.Invoke(time, new object[] { fixedDt });
        return time;
    }

    /// <summary>Finds the repository root from the solution file.</summary>
    /// <returns>The absolute repository root path.</returns>
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "SSSuperGame.slnx")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Repo root not found.");
    }

    /// <summary>Creates the real server physics system with test dependencies.</summary>
    /// <param name="world">The ECS world used by the test.</param>
    /// <param name="physics">The physics service used by the test.</param>
    /// <param name="time">The engine time service.</param>
    /// <returns>The constructed physics system.</returns>
    private static object NewPhysics(EcsDefaultWorld world, IPhysicsWorld2D physics, Time time)
    {
        string path = Path.Combine(RepoRoot(), "Source", "SSSuperGame.Server", "bin", "Debug", "net10.0", "SSSuperGame.Server.dll");
        Assert.True(File.Exists(path), "Build the Server project first.");
        Assembly server = Assembly.LoadFrom(path);
        Type? t = server.GetType("SSSuperGame.Server.CoinRush.PlayerPhysicsSystem");
        Assert.NotNull(t);
        object logger = Activator.CreateInstance(typeof(TestLogger<>).MakeGenericType(t!))!;
        return Activator.CreateInstance(t!, world, physics, time, logger!)!;
    }

    /// <summary>Adds a controllable player for a physics test.</summary>
    /// <param name="world">The ECS world used by the test.</param>
    /// <param name="x">The horizontal spawn coordinate.</param>
    /// <param name="y">The vertical spawn coordinate.</param>
    /// <returns>The new player entity ID.</returns>
    private static int SpawnRunner(EcsDefaultWorld world, float x, float y)
    {
        int e = world.NewEntity();
        world.GetPool<Player>().Add(e) = new Player { Index = 0, PeerId = 0 };
        world.GetPool<MoveState>().Add(e) = new MoveState { LastInputSequence = -1 };
        world.GetPool<NetState>().Add(e) = new NetState();
        world.GetPool<Transform2D>().Add(e) = new Transform2D { Position = new Vector2d(x, y) };
        world.GetPool<InputLatch>().Add(e) = new InputLatch { Sequence = -1 };
        return e;
    }

    /// <summary>Verifies a held movement axis advances on every fixed tick.</summary>
    [Fact]
    public void Held_axis_advances_every_tick_without_gaps()
    {
        var world = new EcsDefaultWorld();
        try
        {
            var physics = new StubPhysics();
            var time = NewTime(1.0 / 60.0);
            object sys = NewPhysics(world, physics, time);
            var fixedUpdate = sys.GetType().GetMethod("FixedUpdate");
            Assert.NotNull(fixedUpdate);

            int e = SpawnRunner(world, 0f, 0f);
            var latchPool = world.GetPool<InputLatch>();
            var transformPool = world.GetPool<Transform2D>();

            const int ticks = 120;
            var xs = new float[ticks];
            for (int i = 0; i < ticks; i++)
            {
                ref InputLatch latch = ref latchPool.Get(e);
                latch.MoveAxis = 1f;
                latch.Jump = 0;
                latch.HasInput = 1;
                latch.Sequence = i;
                fixedUpdate!.Invoke(sys, null);
                xs[i] = (float)transformPool.Read(e).Position.X;
            }

            for (int i = 1; i < ticks; i++)
            {
                Assert.True(xs[i] - xs[i - 1] > 0.1f, $"tick {i} stalled: {xs[i - 1]} -> {xs[i]}");
            }
            Assert.True(xs[ticks - 1] > 14f, $"total distance too short: {xs[ticks - 1]}");
        }
        finally
        {
            world.Destroy();
        }
    }

    /// <summary>Verifies one latched jump press fires during fallback physics.</summary>
    [Fact]
    public void Single_jump_edge_fires_from_latch()
    {
        var world = new EcsDefaultWorld();
        try
        {
            var physics = new StubPhysics();
            var time = NewTime(1.0 / 60.0);
            object sys = NewPhysics(world, physics, time);
            var fixedUpdate = sys.GetType().GetMethod("FixedUpdate");
            Assert.NotNull(fixedUpdate);

            int e = SpawnRunner(world, 0f, 0f);
            var latchPool = world.GetPool<InputLatch>();
            var transformPool = world.GetPool<Transform2D>();

            ref InputLatch latch = ref latchPool.Get(e);
            latch.MoveAxis = 0f;
            latch.Jump = 1;
            latch.HasInput = 1;
            latch.Sequence = 0;
            fixedUpdate!.Invoke(sys, null);

            float y = (float)transformPool.Read(e).Position.Y;
            Assert.Equal(MatchRules.JumpVelocity() / 60f, y, precision: 3);
            Assert.Equal(y, world.GetPool<NetState>().Read(e).Y, precision: 3);
            fixedUpdate.Invoke(sys, null);
            Assert.True(transformPool.Read(e).Position.Y > y, "jump must continue rising on the next tick");
        }
        finally
        {
            world.Destroy();
        }
    }
}
