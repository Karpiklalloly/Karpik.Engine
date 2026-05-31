using DCFApixels.DragonECS;
using DragonExtensions;
using Karpik.Engine.Core;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.ECS;
using Karpik.Jobs;

var tests = new (string Name, Action Run)[]
{
    ("DependencyGraph_Empty_ReturnsEmptyGraph", DependencyGraph_Empty_ReturnsEmptyGraph),
    ("DependencyGraph_DisjointWrites_DoNotCreateDependency", DependencyGraph_DisjointWrites_DoNotCreateDependency),
    ("DependencyGraph_WriteThenRead_CreatesDependency", DependencyGraph_WriteThenRead_CreatesDependency),
    ("DependencyGraph_ReadThenWrite_CreatesDependency", DependencyGraph_ReadThenWrite_CreatesDependency),
    ("DependencyGraph_WriteThenWrite_CreatesDependency", DependencyGraph_WriteThenWrite_CreatesDependency),
    ("DependencyGraph_UnknownAccess_CreatesSequentialBarrier", DependencyGraph_UnknownAccess_CreatesSequentialBarrier),
    ("DependencyGraph_Conflicts_PreserveRegistrationOrder", DependencyGraph_Conflicts_PreserveRegistrationOrder),
    ("World_AddEnabledAsync_StoresInputBeforeEnableAndPersistsResult", World_AddEnabledAsync_StoresInputBeforeEnableAndPersistsResult),
    ("World_AddEnabled_StoresInputBeforeEnableAndPersistsResult", World_AddEnabled_StoresInputBeforeEnableAndPersistsResult),
    ("World_EnableVariants_PersistLifecycleResult", World_EnableVariants_PersistLifecycleResult),
    ("World_DisableVariants_PersistLifecycleResult", World_DisableVariants_PersistLifecycleResult),
    ("World_DelEnabledAsync_DisablesBeforeDeletingComponent", World_DelEnabledAsync_DisablesBeforeDeletingComponent),
    ("World_DelEnabled_DisablesBeforeDeletingComponent", World_DelEnabled_DisablesBeforeDeletingComponent),
    ("World_LifecycleFailures_AreWrappedWithDiagnostics", World_LifecycleFailures_AreWrappedWithDiagnostics)
};

try
{
    foreach (var test in tests)
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
}
finally
{
    LifecycleFixture.Shared.Dispose();
}

static void DependencyGraph_Empty_ReturnsEmptyGraph()
{
    SystemExecutionNode[] nodes = CreateGraph();

    if (nodes.Length != 0)
    {
        throw new InvalidOperationException($"Expected empty graph, got {nodes.Length} nodes.");
    }
}

static void DependencyGraph_DisjointWrites_DoNotCreateDependency()
{
    SystemExecutionNode[] nodes = CreateGraph(new WriteA(), new WriteB());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1]);
}

static void DependencyGraph_WriteThenRead_CreatesDependency()
{
    SystemExecutionNode[] nodes = CreateGraph(new WriteA(), new ReadA());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1], nodes[0]);
}

static void DependencyGraph_ReadThenWrite_CreatesDependency()
{
    SystemExecutionNode[] nodes = CreateGraph(new ReadA(), new WriteA());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1], nodes[0]);
}

static void DependencyGraph_WriteThenWrite_CreatesDependency()
{
    SystemExecutionNode[] nodes = CreateGraph(new WriteA(), new WriteA());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1], nodes[0]);
}

static void DependencyGraph_UnknownAccess_CreatesSequentialBarrier()
{
    SystemExecutionNode[] nodes = CreateGraph(new WriteA(), new UnknownAccess(), new WriteB());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1], nodes[0]);
    AssertDependencies(nodes[2], nodes[1]);
}

static void DependencyGraph_Conflicts_PreserveRegistrationOrder()
{
    SystemExecutionNode[] nodes = CreateGraph(new WriteA(), new ReadA(), new WriteA());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1], nodes[0]);
    AssertDependencies(nodes[2], nodes[0], nodes[1]);
}

static void World_AddEnabledAsync_StoresInputBeforeEnableAndPersistsResult()
{
    var fixture = LifecycleFixture.Shared;
    ComponentLifecycleTrace.Reset();
    var entity = fixture.World.New();
    var pool = fixture.Backend.GetPool<LifecycleComponent>();

    fixture.World
        .AddEnabledAsync(entity.ID, new LifecycleComponent { Value = 7 }, pool)
        .GetAwaiter()
        .GetResult();

    AssertEqual(1, ComponentLifecycleTrace.EnableCalls, "Enable call count");
    AssertEqual(7, ComponentLifecycleTrace.PoolValueObservedByEnable, "Value visible in pool during EnableAsync");
    AssertLifecycleContext(fixture, entity.ID);
    AssertLifecycleComponent(pool.Get(entity.ID), expectedValue: 17, expectedEnableCount: 1, expectedDisableCount: 0);
}

static void World_AddEnabled_StoresInputBeforeEnableAndPersistsResult()
{
    var fixture = LifecycleFixture.Shared;
    ComponentLifecycleTrace.Reset();
    var entity = fixture.World.New();
    var pool = fixture.Backend.GetPool<LifecycleComponent>();

    fixture.World.AddEnabled(entity.ID, new LifecycleComponent { Value = 11 }, pool);

    AssertEqual(1, ComponentLifecycleTrace.EnableCalls, "Enable call count");
    AssertEqual(11, ComponentLifecycleTrace.PoolValueObservedByEnable, "Value visible in pool during Enable");
    AssertLifecycleContext(fixture, entity.ID);
    AssertLifecycleComponent(pool.Get(entity.ID), expectedValue: 21, expectedEnableCount: 1, expectedDisableCount: 0);
}

static void World_EnableVariants_PersistLifecycleResult()
{
    var fixture = LifecycleFixture.Shared;
    var entity = fixture.World.New();
    var pool = fixture.Backend.GetPool<LifecycleComponent>();
    pool.Add(entity.ID) = new LifecycleComponent { Value = 5 };

    ComponentLifecycleTrace.Reset();
    fixture.World.EnableAsync(entity.ID, pool).GetAwaiter().GetResult();

    AssertEqual(1, ComponentLifecycleTrace.EnableCalls, "Async enable call count");
    AssertLifecycleComponent(pool.Get(entity.ID), expectedValue: 15, expectedEnableCount: 1, expectedDisableCount: 0);

    ComponentLifecycleTrace.Reset();
    fixture.World.Enable(entity.ID, pool);

    AssertEqual(1, ComponentLifecycleTrace.EnableCalls, "Blocking enable call count");
    AssertLifecycleComponent(pool.Get(entity.ID), expectedValue: 25, expectedEnableCount: 2, expectedDisableCount: 0);
}

static void World_DisableVariants_PersistLifecycleResult()
{
    var fixture = LifecycleFixture.Shared;
    var entity = fixture.World.New();
    var pool = fixture.Backend.GetPool<LifecycleComponent>();
    pool.Add(entity.ID) = new LifecycleComponent { Value = 20 };

    ComponentLifecycleTrace.Reset();
    fixture.World.DisableAsync(entity.ID, pool).GetAwaiter().GetResult();

    AssertEqual(1, ComponentLifecycleTrace.DisableCalls, "Async disable call count");
    AssertLifecycleComponent(pool.Get(entity.ID), expectedValue: 17, expectedEnableCount: 0, expectedDisableCount: 1);

    ComponentLifecycleTrace.Reset();
    fixture.World.Disable(entity.ID, pool);

    AssertEqual(1, ComponentLifecycleTrace.DisableCalls, "Blocking disable call count");
    AssertLifecycleComponent(pool.Get(entity.ID), expectedValue: 14, expectedEnableCount: 0, expectedDisableCount: 2);
}

static void World_DelEnabledAsync_DisablesBeforeDeletingComponent()
{
    var fixture = LifecycleFixture.Shared;
    ComponentLifecycleTrace.Reset();
    var entity = fixture.World.New();
    var pool = fixture.Backend.GetPool<LifecycleComponent>();
    pool.Add(entity.ID) = new LifecycleComponent { Value = 23 };

    fixture.World.DelEnabledAsync(entity.ID, pool).GetAwaiter().GetResult();

    AssertEqual(1, ComponentLifecycleTrace.DisableCalls, "Disable call count");
    AssertEqual(23, ComponentLifecycleTrace.PoolValueObservedByDisable, "Value visible in pool during DisableAsync");
    AssertLifecycleContext(fixture, entity.ID);
    AssertEqual(false, pool.Has(entity.ID), "Component presence after DelEnabledAsync");
}

static void World_DelEnabled_DisablesBeforeDeletingComponent()
{
    var fixture = LifecycleFixture.Shared;
    ComponentLifecycleTrace.Reset();
    var entity = fixture.World.New();
    var pool = fixture.Backend.GetPool<LifecycleComponent>();
    pool.Add(entity.ID) = new LifecycleComponent { Value = 29 };

    fixture.World.DelEnabled(entity.ID, pool);

    AssertEqual(1, ComponentLifecycleTrace.DisableCalls, "Disable call count");
    AssertEqual(29, ComponentLifecycleTrace.PoolValueObservedByDisable, "Value visible in pool during Disable");
    AssertLifecycleContext(fixture, entity.ID);
    AssertEqual(false, pool.Has(entity.ID), "Component presence after DelEnabled");
}

static void World_LifecycleFailures_AreWrappedWithDiagnostics()
{
    var cases = new (string Phase, bool AddBeforeInvoke, bool FailEnable, Action<DefaultWorld, int, EcsPool<LifecycleComponent>> Invoke)[]
    {
        ("EnableAsync", true, true, (world, entityId, pool) => world.EnableAsync(entityId, pool).GetAwaiter().GetResult()),
        ("Enable", true, true, (world, entityId, pool) => world.Enable(entityId, pool)),
        ("AddEnabledAsync", false, true, (world, entityId, pool) => world.AddEnabledAsync(entityId, new LifecycleComponent { Value = 31 }, pool).GetAwaiter().GetResult()),
        ("AddEnabled", false, true, (world, entityId, pool) => world.AddEnabled(entityId, new LifecycleComponent { Value = 37 }, pool)),
        ("DisableAsync", true, false, (world, entityId, pool) => world.DisableAsync(entityId, pool).GetAwaiter().GetResult()),
        ("Disable", true, false, (world, entityId, pool) => world.Disable(entityId, pool)),
        ("DelEnabledAsync", true, false, (world, entityId, pool) => world.DelEnabledAsync(entityId, pool).GetAwaiter().GetResult()),
        ("DelEnabled", true, false, (world, entityId, pool) => world.DelEnabled(entityId, pool))
    };

    foreach (var testCase in cases)
    {
        var fixture = LifecycleFixture.Shared;
        ComponentLifecycleTrace.Reset();
        ComponentLifecycleTrace.FailEnable = testCase.FailEnable;
        ComponentLifecycleTrace.FailDisable = !testCase.FailEnable;
        var entity = fixture.World.New();
        var pool = fixture.Backend.GetPool<LifecycleComponent>();
        if (testCase.AddBeforeInvoke)
        {
            pool.Add(entity.ID) = new LifecycleComponent { Value = 41 };
        }

        var exception = AssertThrows<ComponentLifecycleException>(
            () => testCase.Invoke(fixture.World, entity.ID, pool),
            testCase.Phase);

        AssertEqual(testCase.Phase, exception.Phase, "Lifecycle failure phase");
        AssertEqual(fixture.Backend.Name, exception.WorldName, "Lifecycle failure world name");
        AssertEqual(fixture.Backend.ID, exception.WorldId, "Lifecycle failure world id");
        AssertEqual(entity.ID, exception.EntityId, "Lifecycle failure entity id");
        AssertEqual(typeof(LifecycleComponent), exception.ComponentType, "Lifecycle failure component type");
        AssertEqual(typeof(LifecycleFailureException), exception.InnerException?.GetType(), "Lifecycle failure inner exception type");
    }
}

static SystemExecutionNode[] CreateGraph(params IEcsRunParallel[] systems)
{
    return EcsRunParallelRunner.CreateDependencyGraph(systems);
}

static void AssertLifecycleContext(LifecycleFixture fixture, int entityId)
{
    AssertEqual(fixture.Services, ComponentLifecycleTrace.Services, "Lifecycle service container");
    AssertEqual(fixture.Backend, ComponentLifecycleTrace.World, "Lifecycle world");
    AssertEqual(entityId, ComponentLifecycleTrace.EntityId, "Lifecycle entity id");
}

static void AssertLifecycleComponent(
    LifecycleComponent component,
    int expectedValue,
    int expectedEnableCount,
    int expectedDisableCount)
{
    AssertEqual(expectedValue, component.Value, "Lifecycle component value");
    AssertEqual(expectedEnableCount, component.EnableCount, "Lifecycle component enable count");
    AssertEqual(expectedDisableCount, component.DisableCount, "Lifecycle component disable count");
}

static void AssertEqual<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {description} '{expected}', got '{actual}'.");
    }
}

static TException AssertThrows<TException>(Action action, string description) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {description} to throw {typeof(TException).Name}.");
}

static void AssertDependencies(SystemExecutionNode node, params SystemExecutionNode[] expected)
{
    if (node.Dependencies.Count != expected.Length)
    {
        throw new InvalidOperationException(
            $"Expected {expected.Length} dependencies for node {node.Index}, got {node.Dependencies.Count}.");
    }

    for (int i = 0; i < expected.Length; i++)
    {
        if (!ReferenceEquals(expected[i], node.Dependencies[i]))
        {
            throw new InvalidOperationException(
                $"Dependency {i} mismatch for node {node.Index}. Expected node {expected[i].Index}, got node {node.Dependencies[i].Index}.");
        }
    }
}

readonly struct ComponentA : IEcsComponent;
readonly struct ComponentB : IEcsComponent;

sealed class WriteA : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsPool<ComponentA> Component = null!;
    }

    public void RunParallel()
    {
    }
}

sealed class WriteB : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsPool<ComponentB> Component = null!;
    }

    public void RunParallel()
    {
    }
}

sealed class ReadA : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsReadonlyPool<ComponentA> Component = default;
    }

    public void RunParallel()
    {
    }
}

sealed class UnknownAccess : IEcsRunParallel
{
    public void RunParallel()
    {
    }
}

sealed class LifecycleFixture : IDisposable
{
    public static LifecycleFixture Shared { get; } = new();

    public ServiceProvider Services { get; } = new();
    public EcsDefaultWorld Backend { get; } = new();
    public DefaultWorld World { get; }

    private LifecycleFixture()
    {
        World = new DefaultWorld(Backend, Services);
    }

    public void Dispose()
    {
        Backend.Destroy();
    }
}

static class ComponentLifecycleTrace
{
    public static int EnableCalls;
    public static int DisableCalls;
    public static int PoolValueObservedByEnable;
    public static int PoolValueObservedByDisable;
    public static IServiceContainer? Services;
    public static EcsWorld? World;
    public static int EntityId;
    public static bool FailEnable;
    public static bool FailDisable;

    public static void Reset()
    {
        EnableCalls = 0;
        DisableCalls = 0;
        PoolValueObservedByEnable = 0;
        PoolValueObservedByDisable = 0;
        Services = null;
        World = null;
        EntityId = 0;
        FailEnable = false;
        FailDisable = false;
    }

    public static void Observe(ComponentLifecycleContext context)
    {
        Services = context.Services;
        World = context.World;
        EntityId = context.EntityId;
    }
}

struct LifecycleComponent : IEcsComponent, IComponentLifecycleAsync<LifecycleComponent>
{
    public int Value;
    public int EnableCount;
    public int DisableCount;

    public JobHandle<LifecycleComponent> EnableAsync(
        LifecycleComponent component,
        ComponentLifecycleContext context)
    {
        ComponentLifecycleTrace.EnableCalls++;
        ComponentLifecycleTrace.Observe(context);
        ComponentLifecycleTrace.PoolValueObservedByEnable = context.World
            .GetPool<LifecycleComponent>()
            .Get(context.EntityId)
            .Value;

        if (ComponentLifecycleTrace.FailEnable)
        {
            return JobHandle<LifecycleComponent>.FromException(new LifecycleFailureException());
        }

        component.Value += 10;
        component.EnableCount++;
        return JobHandle<LifecycleComponent>.FromResult(component);
    }

    public JobHandle<LifecycleComponent> DisableAsync(
        LifecycleComponent component,
        ComponentLifecycleContext context)
    {
        ComponentLifecycleTrace.DisableCalls++;
        ComponentLifecycleTrace.Observe(context);
        ComponentLifecycleTrace.PoolValueObservedByDisable = context.World
            .GetPool<LifecycleComponent>()
            .Get(context.EntityId)
            .Value;

        if (ComponentLifecycleTrace.FailDisable)
        {
            return JobHandle<LifecycleComponent>.FromException(new LifecycleFailureException());
        }

        component.Value -= 3;
        component.DisableCount++;
        return JobHandle<LifecycleComponent>.FromResult(component);
    }
}

sealed class LifecycleFailureException : Exception;
