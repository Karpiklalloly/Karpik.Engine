using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Xunit;

public sealed class EditorSnapshotTests
{
    [Fact]
    public void CaptureEditorSnapshot_ReturnsEntityAndReadOnlyComponentValues()
    {
        var world = new EcsDefaultWorld();
        var runner = new EngineRunner();
        runner.RegisterModule(new EditorSnapshotWorldModuleInstaller(world));

        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        runner.Setup(new Application(Side.Client), scheduler);
        scheduler.Execute();

        var entity = world.NewEntity();
        world.GetPool<EditorSnapshotPosition>().Add(entity) = new EditorSnapshotPosition
        {
            X = 12,
            Y = -4
        };

        var snapshot = runner.CaptureEditorSnapshot();

        var entitySnapshot = Assert.Single(snapshot.Entities);
        Assert.Equal(entity, entitySnapshot.EntityId);
        var component = Assert.Single(entitySnapshot.Components);
        Assert.Contains(nameof(EditorSnapshotPosition), component.TypeName, StringComparison.Ordinal);
        Assert.Contains("X = 12", component.DisplayValue, StringComparison.Ordinal);
        Assert.Contains("Y = -4", component.DisplayValue, StringComparison.Ordinal);

        runner.Destroy();
    }

    [Fact]
    public void CaptureEditorSnapshot_TruncatesEntityCountAndDisplayValues()
    {
        var world = new EcsDefaultWorld();
        var runner = new EngineRunner();
        runner.RegisterModule(new EditorSnapshotWorldModuleInstaller(world));
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        runner.Setup(new Application(Side.Client), scheduler);
        scheduler.Execute();

        for (var i = 0; i <= EditorSnapshotLimits.MaxEntities; i++)
        {
            int created = world.NewEntity();
            world.GetPool<EditorSnapshotPosition>().Add(created);
            if (i == 0)
            {
                world.GetPool<EditorSnapshotLongDisplay>().Add(created);
            }
        }

        EditorRuntimeSnapshot snapshot = runner.CaptureEditorSnapshot();

        Assert.Equal(EditorSnapshotLimits.MaxEntities, snapshot.Entities.Length);
        Assert.True(snapshot.IsTruncated);
        Assert.All(
            snapshot.Entities.SelectMany(item => item.Components),
            component => Assert.True(component.DisplayValue.Length <= EditorSnapshotLimits.MaxDisplayValueLength));
        Assert.Equal(
            EditorSnapshotLimits.MaxDisplayValueLength,
            snapshot.Entities
                .SelectMany(item => item.Components)
                .Single(component => component.TypeName.Contains(nameof(EditorSnapshotLongDisplay), StringComparison.Ordinal))
                .DisplayValue.Length);

        runner.Destroy();
    }

    [Fact]
    public void CaptureEditorSnapshot_WhenComponentGetterThrows_FormatsBoundedError()
    {
        var world = new EcsDefaultWorld();
        var runner = new EngineRunner();
        runner.RegisterModule(new EditorSnapshotWorldModuleInstaller(world));
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        runner.Setup(new Application(Side.Client), scheduler);
        scheduler.Execute();
        int entity = world.NewEntity();
        world.GetPool<EditorSnapshotThrowingGetter>().Add(entity);

        EditorRuntimeSnapshot snapshot = runner.CaptureEditorSnapshot();

        EditorComponentSnapshot component = Assert.Single(Assert.Single(snapshot.Entities).Components);
        Assert.Contains("Value = <error:", component.DisplayValue, StringComparison.Ordinal);
        Assert.True(component.DisplayValue.Length <= EditorSnapshotLimits.MaxDisplayValueLength);
        runner.Destroy();
    }

    [Fact]
    public void EditorSnapshotSerialization_RoundTripsThroughSourceGeneratedContext()
    {
        // NativeAOT hosts disable reflection-based System.Text.Json; the IPC
        // snapshot channel must serialize through the source-generated context.
        var snapshot = new EditorRuntimeSnapshot
        {
            CapturedAtUnixMilliseconds = 1234,
            TotalEntityCount = 1,
            Entities =
            [
                new EditorEntitySnapshot
                {
                    EntityId = 7,
                    Components = [new EditorComponentSnapshot { TypeName = "GameComponent", DisplayValue = "42" }]
                }
            ]
        };

        byte[] payload = snapshot.Serialize();

        Assert.Equal(
            payload,
            global::System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                snapshot, typeof(EditorRuntimeSnapshot), EditorSnapshotJsonContext.Default));
        EditorRuntimeSnapshot restored = EditorRuntimeSnapshot.Deserialize(payload);
        Assert.Equal(snapshot.CapturedAtUnixMilliseconds, restored.CapturedAtUnixMilliseconds);
        Assert.Equal(snapshot.TotalEntityCount, restored.TotalEntityCount);
        EditorEntitySnapshot entity = Assert.Single(restored.Entities);
        Assert.Equal(7, entity.EntityId);
        EditorComponentSnapshot component = Assert.Single(entity.Components);
        Assert.Equal("GameComponent", component.TypeName);
        Assert.Equal("42", component.DisplayValue);
    }
}

internal readonly struct EditorSnapshotPosition : IEcsComponent
{
    public int X { get; init; }
    public int Y { get; init; }
}

internal readonly struct EditorSnapshotLongDisplay : IEcsComponent
{
    public override string ToString() => new('x', EditorSnapshotLimits.MaxDisplayValueLength + 100);
}

internal readonly struct EditorSnapshotThrowingGetter : IEcsComponent
{
    public int Value => throw new InvalidOperationException("broken getter");
}

[Module(ModuleScope.Simulation)]
internal sealed class EditorSnapshotWorldModuleInstaller(EcsDefaultWorld world) : IModuleInstaller
{
    public string Name => nameof(EditorSnapshotWorldModuleInstaller);

    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.RegisterInstance(world).AsSelf().SingleInstance();
    }
}
