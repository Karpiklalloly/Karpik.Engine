using DCFApixels.DragonECS;
using Karpik.Content.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Xunit;
using WireOutcome = Karpik.Content.Transport.ContentReloadOutcome;

public sealed partial class ContentReloadHostTests
{
    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, false)]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, true)]
    public async Task ModDependency_EndToEndKeepsLuaAndContentInOrderAcrossReloads(Side side, bool staticMode)
    {
        using var tree = new HostTree(side);
        using var initial = tree.Build(1);
        tree.BuildMod("z-provider", 3, contentOnly: false);
        tree.BuildMod("a-consumer", 2, contentOnly: false);
        AssetId icon = tree.BuildMod("z-icons", 4);
        tree.DeclareStartupDependencies("z-icons", "{}");
        tree.DeclareStartupDependencies("a-consumer", """{"shared":[{"id":"z-provider"},{"id":"z-icons","minVersion":"1.0.0"}]}""", contentOnly: false);
        const string checks = "assert(has_dependency('z-provider') and has_dependency('z-icons'))\n";
        tree.WriteStartupLua("a-consumer", "Shared", checks + "return {}");

        var observation = new Observation { LoadLua = true };
        var runner = new EngineRunner();
        runner.EnableLocalCodeReload();
        runner.EnableContentReload("test-host");
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        try
        {
            tree.RunDependencyStartup(runner, observation, side, staticMode, scheduler: scheduler);
            var lifecycle = observation.Lifecycle!;
            Assert.Equal(["z-provider", "a-consumer"], lifecycle.Containers.Select(mod => mod.ModDefinition.MetaData.Id));
            var publicDefinitions = observation.Mods!.GetMods().ToArray();
            Assert.Equal(lifecycle.Containers.Select(mod => mod.ModDefinition).ToArray(), publicDefinitions);
            Assert.Equal(["z-icons", "z-provider"], publicDefinitions[1].ResolvedDirectDependencies!.Order(StringComparer.Ordinal));
            var originalRuntime = lifecycle.Containers[1].Runtime;
            var world = observation.Bindings!.World;
            int entity = world.NewEntity();
            var identity = world.GetEntityLong(entity);
            var registry = observation.Registry!;
            Assert.True(registry.TryResolve<string>(tree.AssetId, out var originalAsset));
            Assert.Equal(1u, originalAsset.Version);

            tree.DeclareStartupDependencies("a-consumer", "{}", contentOnly: false);
            tree.WriteStartupLua("a-consumer", "Shared", checks + "return { on_reload = function() " + checks + "end }");
            var reload = runner.RequestCodeReloadAsync();
            scheduler.RunUntilCompleted(reload);
            Assert.Equal(SimulationCodeReloadStatus.Success, (await reload).Status);
            var reloadedRuntime = lifecycle.Containers[1].Runtime;
            Assert.NotSame(originalRuntime, reloadedRuntime);
            Assert.Equal(["z-provider", "a-consumer"], lifecycle.Containers.Select(mod => mod.ModDefinition.MetaData.Id));
            Assert.Equal((2, 1u), await ReadIdAsync(registry, tree.AssetId));

            using var nextBase = tree.Build(6);
            var baseReload = tree.Deliver(nextBase.GenerationId!);
            await PumpUntilAsync(runner, baseReload);
            Assert.Equal(WireOutcome.Applied, (await baseReload).Outcome);
            Assert.Equal("{\"value\":6}", ReadLegacy(registry));
            Assert.Equal((2, 1u), await ReadIdAsync(registry, tree.AssetId));

            tree.BuildMod("z-icons", 5);
            tree.DeclareRefreshDependencies("z-icons", "{}", version: "1.1.0");
            AssertApplied(await RefreshAsync(runner));
            var committedIcon = await ReadIdAsync(registry, icon);
            Assert.Equal(5, committedIcon.Value);
            ulong sequence = observation.Source!.Sequence;

            DeleteMod(tree, "z-icons");
            Assert.Equal(ModContentRefreshStatus.Failed, (await RefreshAsync(runner)).Status);
            Assert.Equal(committedIcon, await ReadIdAsync(registry, icon));
            Assert.Equal(sequence, observation.Source.Sequence);
            Assert.True(registry.TryResolve<string>(tree.AssetId, out var unchanged));
            Assert.Equal(originalAsset, unchanged);
            Assert.Same(reloadedRuntime, lifecycle.Containers[1].Runtime);
            Assert.Same(world, observation.Bindings.World);
            Assert.Equal(identity, world.GetEntityLong(entity));
        }
        finally { await runner.DestroyAsync(); }
    }
}
