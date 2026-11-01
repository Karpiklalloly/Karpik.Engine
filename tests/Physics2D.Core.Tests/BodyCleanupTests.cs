using Autofac;
using DCFApixels.DragonECS;
using DragonExtensions;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Physics.Aether2D;
using Karpik.Engine.Shared.Physics.Core;
using OpenTK.Mathematics;
using Xunit;
using PhysicsWorld = nkast.Aether.Physics2D.Dynamics.World;

public sealed class BodyCleanupTests
{
    [Fact]
    public void DeleteEntity_RemovesBodyBeforeNextPhysicsStep()
    {
        using var fixture = new Fixture();
        int entity = fixture.AddBody();
        Assert.Single(fixture.Backend.BodyList);

        fixture.World.Del(entity);

        Assert.Empty(fixture.Backend.BodyList);
        Span<RaycastHit2D> hits = stackalloc RaycastHit2D[4];
        Assert.Equal(0, fixture.Physics.OverlapCircle(Vector2d.Zero, 2, uint.MaxValue, hits));
        fixture.Physics.Step(1.0 / 60);
        fixture.Cleanup.LateUpdate();
        Assert.Empty(fixture.Backend.BodyList);
    }

    [Fact]
    public void DestroyRequestThenEntityDeletion_DoesNotDestroyReusedHandle()
    {
        using var fixture = new Fixture();
        int first = fixture.AddBody();
        var oldHandle = fixture.World.Get<PhysicsBodyRef>(first).Handle;
        fixture.World.Add(first, new DestroyBodyRequest());
        fixture.Cleanup.LateUpdate();
        int second = fixture.AddBody();
        Assert.Equal(oldHandle, fixture.World.Get<PhysicsBodyRef>(second).Handle);

        fixture.World.Del(first);

        Assert.Single(fixture.Backend.BodyList);
        fixture.World.Del(second);
        Assert.Empty(fixture.Backend.BodyList);
    }

    [Fact]
    public void DeleteEntityWithPendingRequest_DestroysBodyOnce()
    {
        using var fixture = new Fixture();
        int first = fixture.AddBody();
        fixture.World.Add(first, new DestroyBodyRequest());
        fixture.World.Del(first);
        fixture.Cleanup.LateUpdate();
        Assert.Empty(fixture.Backend.BodyList);
        int second = fixture.AddBody();
        Assert.Single(fixture.Backend.BodyList);
        fixture.World.Del(second);
        Assert.Empty(fixture.Backend.BodyList);
    }

    [Fact]
    public void DeleteEntity_WithoutBodyOrWithInvalidHandle_IsSafe()
    {
        using var fixture = new Fixture();
        fixture.World.Del(fixture.World.New());
        var entity = fixture.World.New();
        fixture.World.Add(entity.ID, new PhysicsBodyRef { Handle = PhysicsBodyHandle.Invalid });
        fixture.World.Del(entity);
        Assert.Empty(fixture.Backend.BodyList);
    }

    [Fact]
    public void Cleanup_DestroyAndReinitialize_TracksOnlyActiveLifetime()
    {
        using var fixture = new Fixture();
        int first = fixture.AddBody();
        fixture.Cleanup.Destroy();
        fixture.World.Del(first);
        Assert.Single(fixture.Backend.BodyList);

        fixture.Cleanup.Init();
        int second = fixture.AddBody();
        fixture.World.Del(second);
        Assert.Single(fixture.Backend.BodyList);
    }

    [Fact]
    public void DeleteEntity_AfterWarmup_DoesNotAllocate()
    {
        using var fixture = new Fixture();
        for (int i = 0; i < 16; i++) fixture.World.Del(fixture.AddBody());
        int[] entities = new int[32];
        for (int i = 0; i < entities.Length; i++) entities[i] = fixture.AddBody();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < entities.Length; i++) fixture.World.Del(entities[i]);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Empty(fixture.Backend.BodyList);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IContainer _container = new ContainerBuilder().Build();
        private readonly EcsDefaultWorld _ecs = new();
        public PhysicsWorld Backend { get; } = new();
        public AetherPhysicsWorld Physics { get; }
        public DefaultWorld World { get; }
        public Physics2DBodyDestroyer Cleanup { get; }

        public Fixture()
        {
            Physics = new AetherPhysicsWorld(Backend);
            World = new DefaultWorld(_ecs, new AutofacServiceResolver(_container));
            Cleanup = new Physics2DBodyDestroyer(Physics, World);
            Cleanup.Init();
        }

        public int AddBody()
        {
            int entity = World.New().ID;
            var config = new BodyConfig { Type = BodyType.Dynamic, Mass = 1, CategoryBits = 1, MaskBits = uint.MaxValue };
            var shape = ShapeConfig.Circle(0.5);
            var handle = Physics.CreateBody(entity, Vector2d.Zero, 0, in config, in shape);
            World.Add(entity, new PhysicsBodyRef { Handle = handle });
            return entity;
        }

        public void Dispose()
        {
            Cleanup.Destroy();
            _ecs.Destroy();
            _container.Dispose();
        }
    }
}
