using Karpik.Content.Transport;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.Modding.Lua;
using Microsoft.Extensions.Logging;
using Xunit;

public sealed partial class ContentReloadHostTests
{
    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, false)]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, true)]
    public async Task ModActivation_ChangesLuaAndContentTogetherOnlyAfterRestart(Side side, bool staticMode)
    {
        using var tree = new HostTree(side);
        using var initial = tree.Build(1);
        var firstAsset = tree.BuildMod("first", 2, contentOnly: false);
        var secondAsset = tree.BuildMod("second", 3, contentOnly: false);
        string config = Path.Combine(tree.Root, "mods.json");
        File.WriteAllText(config, """{"disabled":["second"]}""");
        var observation = new Observation { LoadLua = true };
        var runner = new EngineRunner();
        runner.EnableLocalCodeReload();
        runner.EnableContentReload("test-host");
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        try
        {
            tree.RunDependencyStartup(runner, observation, side, staticMode, scheduler: scheduler);
            Assert.Equal("first", Assert.Single(observation.Mods!.GetMods()).MetaData.Id);
            Assert.False(observation.Registry!.TryResolve<string>(secondAsset, out _));

            // Pump code reload on the owning thread, before the first async read can change it.
            File.WriteAllText(config, """{"disabled":["first"]}""");
            var reload = runner.RequestCodeReloadAsync();
            scheduler.RunUntilCompleted(reload);
            Assert.Equal(SimulationCodeReloadStatus.Success, (await reload).Status);
            Assert.Equal("first", Assert.Single(observation.Lifecycle!.Containers).ModDefinition.MetaData.Id);
            using var nextBase = tree.Build(4);
            var baseReload = tree.Deliver(nextBase.GenerationId!);
            await PumpUntilAsync(runner, baseReload);
            Assert.Equal(ContentReloadOutcome.Applied, (await baseReload).Outcome);
            Assert.Equal("{\"value\":4}", ReadLegacy(observation.Registry));
            Assert.Equal((2, 1u), await ReadIdAsync(observation.Registry, tree.AssetId));
            Assert.False(observation.Registry.TryResolve<string>(secondAsset, out _));
        }
        finally { await runner.DestroyAsync(); }

        var restarted = new Observation { LoadLua = true };
        var newRunner = new EngineRunner();
        try
        {
            tree.RunDependencyStartup(newRunner, restarted, side, staticMode);
            Assert.Equal("second", Assert.Single(restarted.Mods!.GetMods()).MetaData.Id);
            Assert.Equal("second", Assert.Single(restarted.Lifecycle!.Containers).ModDefinition.MetaData.Id);
            Assert.Equal((3, 1u), await ReadIdAsync(restarted.Registry!, tree.AssetId));
            Assert.False(restarted.Registry!.TryResolve<string>(firstAsset, out _));
        }
        finally { await newRunner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModActivation_UsesSelectionInLuaOnlyHost()
    {
        using var tree = new HostTree(Side.Server);
        tree.DeclareStartupDependencies("disabled", "{}", contentOnly: false);
        tree.WriteStartupLua("disabled", "Server", "error('disabled Lua ran')");
        tree.DeclareStartupDependencies("consumer", """{"server":[{"id":"disabled","optional":true}]}""", contentOnly: false);
        tree.WriteStartupLua("consumer", "Server", "assert(not has_dependency('disabled')) return {}");
        File.WriteAllText(Path.Combine(tree.Root, "mods.json"), """{"disabled":["disabled"]}""");
        var observation = new Observation { LoadLua = true };
        var runner = new EngineRunner();
        try
        {
            tree.RunDependencyStartup(runner, observation, Side.Server, staticMode: false, luaOnly: true);
            Assert.Equal("consumer", Assert.Single(observation.Mods!.GetMods()).MetaData.Id);
            Assert.Null(observation.Registry);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModActivation_DisabledRequiredProviderRejectsStartupBeforeSimulation()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        tree.BuildMod("provider", 2);
        tree.DeclareStartupDependencies("consumer", """{"shared":[{"id":"provider"}]}""", contentOnly: false);
        tree.WriteStartupLua("consumer", "Shared");
        File.WriteAllText(Path.Combine(tree.Root, "mods.json"), """{"disabled":["provider"]}""");
        var observation = new Observation { LoadLua = true };
        var runner = new EngineRunner();
        try
        {
            var error = Assert.Throws<ModDependencyException>(() => tree.RunDependencyStartup(runner, observation, Side.Server, staticMode: false));
            Assert.Contains("consumer", error.Message);
            Assert.Contains("provider", error.Message);
            Assert.Contains("no payload", error.Message);
            Assert.Null(observation.Lifecycle);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModActivation_RefreshUsesStartupChoiceEvenAfterConfigChanges()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var disabledAsset = tree.BuildMod("z-disabled", 9);
        tree.BuildMod("active", 2);
        string config = Path.Combine(tree.Root, "mods.json");
        File.WriteAllText(config, """{"disabled":["z-disabled"]}""");
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            var registry = observation.Registry!;
            Assert.Equal((2, 1u), await ReadIdAsync(registry, tree.AssetId));
            File.WriteAllText(config, """{"disabled":["active"]}""");
            tree.BuildMod("active", 3);
            tree.BuildMod("patch", 4, targetOwner: "z-disabled", targetId: disabledAsset);
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((3, 2u), await ReadIdAsync(registry, tree.AssetId));
            Assert.False(registry.TryResolve<string>(disabledAsset, out _));
            Assert.Contains(observation.Logs.Messages, log => log.Level == LogLevel.Warning &&
                log.Message.Contains("Target owner 'z-disabled' is not installed.", StringComparison.Ordinal));

            File.WriteAllText(config, "not JSON");
            tree.BuildMod("active", 5);
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((5, 3u), await ReadIdAsync(registry, tree.AssetId));
            Assert.False(registry.TryResolve<string>(disabledAsset, out _));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModActivation_ExcludesDisabledCookedOwnerAndReportsSkippedOverride()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var disabledAsset = tree.BuildMod("disabled", 2, contentOnly: false);
        tree.DeclareStartupDependencies("disabled", """{"shared":[{"id":"missing"}]}""", contentOnly: false);
        tree.WriteStartupLua("disabled", "Shared", "error('disabled Lua ran')");
        var patchAsset = tree.BuildMod("patch", 3, targetOwner: "disabled", targetId: disabledAsset);
        tree.DeclareStartupDependencies("observer", """{"shared":[{"id":"disabled","optional":true}]}""", contentOnly: false);
        tree.WriteStartupLua("observer", "Shared", "assert(not has_dependency('disabled')) return {}");
        File.WriteAllText(Path.Combine(tree.Root, "mods.json"), """{"disabled":["disabled"]}""");
        var observation = new Observation { LoadLua = true };
        using var logging = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning).AddProvider(observation.Logs));
        var runner = new EngineRunner(logging);
        try
        {
            tree.RunDependencyStartup(runner, observation, Side.Server, staticMode: false);
            Assert.Equal("observer", Assert.Single(observation.Mods!.GetMods()).MetaData.Id);
            Assert.False(observation.Registry!.TryResolve<string>(disabledAsset, out _));
            Assert.Equal((3, 1u), await ReadIdAsync(observation.Registry, patchAsset));
            Assert.Equal((1, 1u), await ReadIdAsync(observation.Registry, tree.AssetId));
            Assert.Contains(observation.Logs.Messages, log => log.Level == LogLevel.Warning &&
                log.Message.Contains("Target owner 'disabled' is not installed.", StringComparison.Ordinal));
        }
        finally { await runner.DestroyAsync(); }
    }
}
