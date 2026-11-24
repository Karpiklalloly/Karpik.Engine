using System.Text.Json;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Modding.Lua;
using Xunit;

public sealed partial class ContentReloadHostTests
{
    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, false)]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, true)]
    public async Task ModDependencyRefresh_ResolvedOrderSelectsNonOrdinalWinner(Side side, bool staticMode)
    {
        using var tree = new HostTree(side);
        using var initial = tree.Build(1);
        tree.BuildMod("z-provider", 3);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode, enabled: false, modding: true);
        try
        {
            Assert.Equal((3, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            tree.BuildMod("a-patch", 2);
            tree.DeclareRefreshDependencies("a-patch", """{"shared":[{"id":"z-provider"}]}""");
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((2, 2u), await ReadIdAsync(observation.Registry!, tree.AssetId));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModDependencyRefresh_UsesFrozenScriptsAndOnlySelectedContentSources()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        tree.DeclareRefreshDependencies("a-lua", "{}", contentOnly: false, version: "2.0.0");
        tree.WriteStartupLua("a-lua", "Shared");
        var observation = new Observation { LoadLua = true };
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            var owner = Assert.Single(observation.Lifecycle!.Containers);
            var runtime = owner.Runtime;
            var world = observation.Bindings!.World;
            tree.DeclareRefreshDependencies("a-lua", """{"shared":[{"id":"missing"}]}""", contentOnly: false);
            File.Delete(Path.Combine(tree.ModDirectory("a-lua"), "Shared", "main.lua"));
            AssetId ignored = tree.BuildMod("new-script", 99, contentOnly: false);
            tree.DeclareRefreshDependencies("new-script", """{"shared":[{"id":"missing"}]}""", contentOnly: false);
            tree.DeclareRefreshDependencies("no-side-package", """{"shared":[{"id":"also-missing"}]}""");
            tree.BuildMod("z-patch", 2);
            tree.DeclareRefreshDependencies("z-patch", """{"shared":[{"id":"a-lua","minVersion":"2.0.0"},{"id":"new-script","optional":true,"minVersion":"9.0.0"}]}""");
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((2, 2u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.False(observation.Registry!.TryResolve<string>(ignored, out _));
            Assert.Same(owner, Assert.Single(observation.Lifecycle.Containers));
            Assert.Same(runtime, owner.Runtime);
            Assert.Same(world, observation.Bindings.World);

            tree.BuildMod("z-patch", 4);
            tree.DeclareRefreshDependencies("z-patch", """{"shared":[{"id":"new-script"}]}""");
            var failed = await RefreshAsync(runner);
            Assert.Equal(ModContentRefreshStatus.Failed, failed.Status);
            Assert.Contains("new-script", failed.Error);
            Assert.Contains("no payload", failed.Error);
            Assert.Equal((2, 2u), await ReadIdAsync(observation.Registry, tree.AssetId));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModDependencyRefresh_ActiveScriptWithoutFrozenEntryUsesOnlyActualCookedSelection(bool admitted)
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        if (admitted) tree.BuildMod("provider", 2);
        else tree.BuildStartupCollision("provider"); // A successful startup capture but rejected by composition.
        tree.DeclareRefreshDependencies("provider", "{}", contentOnly: false);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            if (admitted) DeleteMod(tree, "provider"); // The missing active Lua path retains an accepted cooked layer.
            else
            {
                // The new entry point is not a frozen script payload. The replacement path keeps the rejected cooked
                // package out of selection, despite startup's provisional cooked availability for this ID.
                DeleteMod(tree, "provider");
                tree.DeclareRefreshDependencies("provider", "{}", contentOnly: false);
                tree.WriteStartupLua("provider", "Shared");
            }
            tree.BuildMod("z-dependent", 3);
            tree.DeclareRefreshDependencies("z-dependent", """{"shared":[{"id":"provider"}]}""");
            var refresh = await RefreshAsync(runner);
            if (admitted)
            {
                AssertApplied(refresh);
                Assert.Equal((3, 2u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            }
            else
            {
                Assert.Equal(ModContentRefreshStatus.Failed, refresh.Status);
                Assert.Contains("no payload", refresh.Error);
                Assert.Contains("provider", refresh.Error);
                Assert.Equal((1, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            }
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(false, false)] // Uninstall a required target.
    [InlineData(true, false)] // Uninstalling an optional target also changes the frozen direct set.
    [InlineData(true, true)] // Install a previously absent optional target.
    public async Task ModDependencyRefresh_RejectsChangingLuaDirectDependency(bool optional, bool installing)
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        if (!installing) tree.BuildMod("z-provider", 2);
        tree.DeclareRefreshDependencies("a-lua", $$"""{"shared":[{"id":"z-provider","optional":{{(optional ? "true" : "false")}}}]}""", contentOnly: false);
        tree.WriteStartupLua("a-lua", "Server");
        var observation = new Observation { LoadLua = true };
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            var owner = Assert.Single(observation.Lifecycle!.Containers);
            if (installing) tree.BuildMod("z-provider", 2);
            else DeleteMod(tree, "z-provider");
            var refresh = await RefreshAsync(runner);
            Assert.Equal(ModContentRefreshStatus.Failed, refresh.Status);
            Assert.Contains("Server", refresh.Error);
            Assert.Contains("a-lua", refresh.Error);
            Assert.Contains("z-provider", refresh.Error);
            Assert.Contains(optional ? "Restart required" : "required but no payload", refresh.Error);
            Assert.Equal((installing ? 1 : 2, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.Equal(0UL, observation.Source!.Sequence);
            Assert.Same(owner, Assert.Single(observation.Lifecycle.Containers));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModDependencyRefresh_RejectsChangingRelativeLuaOrderWithUnchangedDirectSets()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        tree.BuildMod("0-provider", 2);
        tree.DeclareRefreshDependencies("a-lua", """{"shared":[{"id":"0-provider"}]}""", contentOnly: false);
        tree.DeclareRefreshDependencies("b-lua", "{}", contentOnly: false);
        tree.WriteStartupLua("a-lua", "Shared");
        tree.WriteStartupLua("b-lua", "Shared");
        var observation = new Observation { LoadLua = true };
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            var owners = observation.Lifecycle!.Containers.ToArray();
            Assert.Equal(["a-lua", "b-lua"], owners.Select(static owner => owner.ModDefinition.MetaData.Id));
            Assert.Equal("0-provider", Assert.Single(owners[0].ModDefinition.ResolvedDirectDependencies!));
            Assert.Empty(owners[1].ModDefinition.ResolvedDirectDependencies!);
            // This only changes the provider's set, but it now orders b-lua before both the provider and a-lua.
            tree.BuildMod("0-provider", 3);
            tree.DeclareRefreshDependencies("0-provider", """{"shared":[{"id":"b-lua"}]}""");
            var refresh = await RefreshAsync(runner);
            Assert.Equal(ModContentRefreshStatus.Failed, refresh.Status);
            Assert.Contains("Restart required", refresh.Error);
            Assert.Contains("order", refresh.Error);
            Assert.Contains("a-lua", refresh.Error);
            Assert.Contains("b-lua", refresh.Error);
            Assert.Equal((2, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.Equal(0UL, observation.Source!.Sequence);
            Assert.Equal(owners, observation.Lifecycle.Containers);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModDependencyRefresh_PublishesMetadataOnlyAndRetainsItAcrossReclassification(bool linked)
    {
        if (linked && !OperatingSystem.IsLinux()) return;
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        const string originalRules = """{"shared":[{"id":"missing-a","optional":true,"minVersion":"1.0.0"},{"id":"missing-b","optional":true}]}""";
        tree.BuildMod("provider", 2);
        tree.DeclareRefreshDependencies("provider", originalRules);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: linked, enabled: false, modding: true);
        ModContentPackage committed;
        try
        {
            var registry = observation.Registry!;
            var original = ReadCommittedPackage(registry, "provider");
            Assert.True(registry.TryResolve<string>(tree.AssetId, out var assetRef));
            await registry.LoadAsync(assetRef);
            Assert.True(registry.TryGet(assetRef, out var before));
            string originalPayload = before.Payload;

            tree.DeclareRefreshDependencies("provider", originalRules, version: "2.0.0");
            AssertApplied(await RefreshAsync(runner));
            committed = ReadCommittedPackage(registry, "provider");
            Assert.NotSame(original, committed);
            Assert.Same(original.Store, committed.Store);
            Assert.True(registry.TryResolve<string>(tree.AssetId, out var sameRef));
            Assert.Equal(assetRef, sameRef);
            Assert.True(registry.TryGet(assetRef, out var after));
            Assert.Same(originalPayload, after.Payload);
            Assert.Equal(0UL, observation.Source!.Sequence);

            tree.DeclareRefreshDependencies("provider", """{"shared":[{"optional":true,"id":"missing-b"},{"id":"missing-a","minVersion":"01.0.0","optional":true}],"client":[]}""", version: "2.0.0");
            AssertApplied(await RefreshAsync(runner));
            Assert.Same(committed, ReadCommittedPackage(registry, "provider"));
            Assert.Equal(0UL, observation.Source.Sequence);

            if (linked)
            {
                DeleteMod(tree, "provider");
                LinkLegacyMod(tree, "provider");
            }
            tree.DeclareRefreshDependencies("provider", """{"shared":[{"id":"missing-required"}]}""", contentOnly: false);
            tree.BuildMod("z-dependent", 3);
            tree.DeclareRefreshDependencies("z-dependent", """{"shared":[{"id":"provider","minVersion":"2.0.0"}]}""");
            AssertApplied(await RefreshAsync(runner));
            Assert.Same(committed, ReadCommittedPackage(registry, "provider"));
            Assert.Equal((3, 2u), await ReadIdAsync(registry, tree.AssetId));
            Assert.Contains(observation.Logs.Messages, item => item.Message.Contains("KCR-MOD-RESTART") && item.Message.Contains("provider"));
        }
        finally { await runner.DestroyAsync(); }
        string locator = Assert.Single(committed.Manifest.Entries).ArtifactLocator;
        Assert.Throws<ObjectDisposedException>(() => committed.Store.Get(locator));
    }

    [Fact]
    public async Task ModDependencyRefresh_DeclarationOnlyUpdateReordersUnchangedCookedStores()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        tree.BuildMod("a-patch", 2);
        tree.DeclareRefreshDependencies("a-patch", "{}");
        tree.BuildMod("z-provider", 3);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            var registry = observation.Registry!;
            var original = ReadCommittedPackage(registry, "a-patch");
            var priorProvider = ReadCommittedPackage(registry, "z-provider");
            tree.DeclareRefreshDependencies("a-patch", """{"server":[{"id":"z-provider"}]}""");
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((2, 2u), await ReadIdAsync(registry, tree.AssetId));
            var reordered = ReadCommittedPackage(registry, "a-patch");
            Assert.NotSame(original, reordered);
            Assert.Same(original.Store, reordered.Store);
            Assert.Same(priorProvider, ReadCommittedPackage(registry, "z-provider"));
            Assert.Equal(1UL, observation.Source!.Sequence);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModDependencyRefresh_CompositionFailureLeavesPriorPackageMetadataForRetainedLayer()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        ModContentPackage committed;
        try
        {
            var registry = observation.Registry!;
            tree.BuildMod("provider", 2);
            tree.DeclareRefreshDependencies("provider", "{}");
            AssertApplied(await RefreshAsync(runner));
            committed = ReadCommittedPackage(registry, "provider");
            tree.DeclareRefreshDependencies("provider", "{}", version: "2.0.0");
            tree.BuildStartupCollision("z-collision");
            tree.DeclareRefreshDependencies("z-collision", """{"shared":[{"id":"provider","minVersion":"2.0.0"}]}""");
            var failed = await RefreshAsync(runner);
            Assert.Equal(ModContentRefreshStatus.Failed, failed.Status);
            Assert.Contains("already has an owner", failed.Error); // The new version was visible to this candidate's graph.
            Assert.Same(committed, ReadCommittedPackage(registry, "provider"));
            Assert.Equal(1UL, observation.Source!.Sequence);

            DeleteMod(tree, "z-collision");
            tree.DeclareRefreshDependencies("provider", """{"shared":[{"id":"missing-required"}]}""", contentOnly: false, version: "9.0.0");
            tree.BuildMod("z-dependent", 3);
            tree.DeclareRefreshDependencies("z-dependent", """{"shared":[{"id":"provider","minVersion":"1.0.0","maxVersionExclusive":"2.0.0"}]}""");
            AssertApplied(await RefreshAsync(runner));
            Assert.Same(committed, ReadCommittedPackage(registry, "provider"));
            Assert.Equal((3, 3u), await ReadIdAsync(registry, tree.AssetId));
            string locator = Assert.Single(committed.Manifest.Entries).ArtifactLocator;
            Assert.Equal("{\"value\":2}", System.Text.Encoding.UTF8.GetString(committed.Store.Get(locator).Span));
        }
        finally { await runner.DestroyAsync(); }
        Assert.Throws<ObjectDisposedException>(() => committed.Store.Get(Assert.Single(committed.Manifest.Entries).ArtifactLocator));
    }

    [Fact]
    public async Task ModDependencyRefresh_AbortedMetadataOnlyPreparationKeepsTheCommittedVersionAndStore()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        tree.BuildMod("provider", 2);
        tree.DeclareRefreshDependencies("provider", "{}");
        var observation = new Observation { LoadLua = true };
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        ModContentPackage committed;
        try
        {
            var registry = observation.Registry!;
            committed = ReadCommittedPackage(registry, "provider");
            var mods = (ModsRegistry)observation.Mods!;
            var refresh = new LuaModContentRefresh(mods.ModsDirectoryPath, AssetTarget.Server, mods.StartupDependencies!, [committed]);
            tree.DeclareRefreshDependencies("provider", "{}", version: "2.0.0");
            var (packages, owners, captures) = await refresh.CaptureAsync([committed], _ => { }, default);
            try
            {
                var package = Assert.Single(packages);
                Assert.NotSame(committed, package);
                Assert.Same(committed.Store, package.Store);
                using var update = await registry.PrepareModUpdateAsync(packages, owners);
                update.Dispose(); // Abort without entering the publication boundary.
                await update.Completion;
            }
            finally { ModContentCapture.DisposeAll(captures); }
            Assert.Same(committed, ReadCommittedPackage(registry, "provider"));
            Assert.Equal((2, 1u), await ReadIdAsync(registry, tree.AssetId));
            Assert.Equal(0UL, observation.Source!.Sequence);

            tree.DeclareRefreshDependencies("provider", "{}", contentOnly: false, version: "9.0.0");
            tree.BuildMod("consumer", 3);
            tree.DeclareRefreshDependencies("consumer", """{"shared":[{"id":"provider","minVersion":"1.0.0","maxVersionExclusive":"2.0.0"}]}""");
            var (retry, _, retryCaptures) = await refresh.CaptureAsync([ReadCommittedPackage(registry, "provider")], _ => { }, default);
            try { Assert.Same(committed, Assert.Single(retry, package => package.ModId == "provider")); }
            finally { ModContentCapture.DisposeAll(retryCaptures); }
        }
        finally { await runner.DestroyAsync(); }
        Assert.Throws<ObjectDisposedException>(() => committed.Store.Get(Assert.Single(committed.Manifest.Entries).ArtifactLocator));
    }

    private static ModContentPackage ReadCommittedPackage(ContentRegistry registry, string id)
    {
        // The package identity lives in the internal root and is intentionally absent from the asset API.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        object root = typeof(ContentRegistry).GetProperty("CommittedSnapshot", flags)!.GetValue(registry)!;
        var packages = (IReadOnlyList<ModContentPackage>)root.GetType().GetField("Packages", flags)!.GetValue(root)!;
        return Assert.Single(packages, package => package.ModId == id);
    }

    private sealed partial class HostTree
    {
        public void DeclareRefreshDependencies(string id, string dependencies, bool contentOnly = true, string version = "1.0.0")
        {
            using var manifest = JsonDocument.Parse(dependencies);
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(ModDirectory(id)).FullName, "mod_info.json"),
                JsonSerializer.Serialize(new { id, contentOnly, version, dependencies = manifest.RootElement }));
        }
    }
}
