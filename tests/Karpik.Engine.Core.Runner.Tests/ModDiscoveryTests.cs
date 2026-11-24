using Autofac;
using Karpik.Content.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Collection(nameof(WorkingDirectoryCollection))]
public sealed class ModDiscoveryTests
{
    [Fact]
    public async Task Startup_UsesInjectedFileRootWithoutResolvingLegacyAssets()
    {
        using var tree = new ModTree();
        tree.WriteMod("engine-root", """{"id":"engine-root"}""");
        var builder = new ContainerBuilder();
        builder.RegisterInstance(tree.FromRoot(() => new PhysicalFileSystem())).As<IFileSystem>();
        builder.RegisterInstance(NullLogger<IModsRegistry>.Instance).As<ILogger<IModsRegistry>>();
        AttributedServiceRegistrar.Register(builder, [typeof(ModsRegistry)], ModuleScope.ModSet);
        using var scope = builder.Build();

        var registry = scope.Resolve<IModsRegistry>();
        await registry.Scan(ExecutionSide.Server);

        Assert.Equal("engine-root", Assert.Single(registry.GetMods()).MetaData.Id);
    }

    [Fact]
    public async Task Startup_ReadsOrdinaryMetadataWithoutLegacyAssetsAndOrdersLuaByOrdinalId()
    {
        using var tree = new ModTree();
        tree.WriteMod("folder-first", """{"id":"a","name":"A","author":"Author","version":"0.1","description":"Desc"}""");
        tree.WriteMod("folder-last", """{"id":"Z"}""");
        var registry = tree.CreateRegistry();

        await registry.Scan(ExecutionSide.Client);

        ModDefinition[] mods = registry.GetMods().ToArray();
        Assert.Equal(["Z", "a"], mods.Select(mod => mod.MetaData.Id));
        ModMetaData metadata = mods[1].MetaData;
        Assert.Equal(("A", "Author", "0.1", "Desc"), (metadata.Name, metadata.Author, metadata.Version, metadata.Description));
    }

    [Fact]
    public async Task Startup_ExcludesOnlyExplicitContentOnlyModsFromLua()
    {
        using var tree = new ModTree();
        tree.WriteMod("art", """{"id":"art","contentOnly":true}""");
        tree.WriteMod("legacy", """{"id":"legacy"}""");
        tree.WriteMod("script", """{"id":"script","contentOnly":false}""");
        var registry = tree.CreateRegistry();

        await registry.Scan(ExecutionSide.Server);

        Assert.Equal(["legacy", "script"], registry.GetMods().Select(mod => mod.MetaData.Id));
    }

    [Theory]
    [InlineData("Shared")]
    [InlineData("Client")]
    [InlineData("Server")]
    public async Task Startup_DiagnosesContentOnlyFlagWithAnyLuaEntryPoint(string side)
    {
        using var tree = new ModTree();
        string mod = tree.WriteMod("mixed", """{"id":"mixed","contentOnly":true}""");
        string entry = Path.Combine(Directory.CreateDirectory(Path.Combine(mod, side)).FullName, "main.lua");
        File.WriteAllText(entry, "return {}");
        var logger = new RegistryLogger();
        var registry = tree.CreateRegistry(logger);

        await registry.Scan(ExecutionSide.Server);

        string error = Assert.Single(logger.Errors);
        Assert.Contains("contentOnly", error);
        Assert.Contains(entry, error);
        Assert.Empty(registry.GetMods());
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("null")]
    public async Task Startup_RejectsNonBooleanContentOnlyFlag(string value)
    {
        using var tree = new ModTree();
        tree.WriteMod("invalid-flag", $$"""{"id":"invalid-flag","contentOnly":{{value}}}""");
        var logger = new RegistryLogger();
        var registry = tree.CreateRegistry(logger);

        await registry.Scan(ExecutionSide.Server);

        Assert.Contains("contentOnly must be a JSON boolean", Assert.Single(logger.Errors));
        Assert.Empty(registry.GetMods());
    }

    [Fact]
    public async Task Startup_SkipsOversizedTrailingLockedAndIncompleteMetadataWithoutLosingValidMods()
    {
        using var tree = new ModTree();
        string oversized = tree.WriteMod("oversized", """{"id":"oversized"}""" + new string(' ', ContentCaptureFiles.MaximumMetadataBytes));
        string trailing = tree.WriteMod("trailing", """{"id":"trailing"} {}""");
        string locked = tree.WriteMod("locked", """{"id":"locked"}""");
        using var exclusive = new FileStream(Path.Combine(locked, "mod_info.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        string incomplete = Directory.CreateDirectory(Path.Combine(tree.Root, "Mods", "incomplete")).FullName;
        tree.WriteMod("valid", """{"id":"valid"}""");
        var logger = new RegistryLogger();
        var registry = tree.CreateRegistry(logger);

        await registry.Scan(ExecutionSide.Server);

        Assert.Equal("valid", Assert.Single(registry.GetMods()).MetaData.Id);
        Assert.Equal(4, logger.Errors.Count);
        Assert.Contains(logger.Errors, error => error.Contains(oversized) && error.Contains("exceeds"));
        Assert.Contains(logger.Errors, error => error.Contains(trailing));
        Assert.Contains(logger.Errors, error => error.Contains(locked));
        Assert.Contains(logger.Errors, error => error.Contains(incomplete) && error.Contains("Missing mod_info.json"));
    }

    [Fact]
    public async Task Startup_ReportsWrongKindModsRootButNotAnAbsentRoot()
    {
        using var tree = new ModTree();
        var logger = new RegistryLogger();
        var registry = tree.CreateRegistry(logger);
        await registry.Scan(ExecutionSide.Client);
        Assert.Empty(registry.GetMods());
        Assert.Empty(logger.Errors);

        string mods = Path.Combine(tree.Root, "Mods");
        File.WriteAllText(mods, "not a directory");
        var brokenRegistry = tree.CreateRegistry(logger);
        await brokenRegistry.Scan(ExecutionSide.Client);
        Assert.Empty(brokenRegistry.GetMods());
        Assert.Contains(mods, Assert.Single(logger.Errors));
    }

    [Fact]
    public async Task Startup_RejectsDanglingLinksLinkedMetadataAndUnsafeEntryPaths()
    {
        // Windows symlink creation may require privileges.
        if (!OperatingSystem.IsLinux()) return;
        using var tree = new ModTree();
        string mods = Path.Combine(tree.Root, "Mods");
        string external = Directory.CreateDirectory(Path.Combine(tree.Root, "elsewhere")).FullName;
        var logger = new RegistryLogger();
        Directory.CreateSymbolicLink(mods, external + "-missing");
        var linkedRegistry = tree.CreateRegistry(logger);
        await linkedRegistry.Scan(ExecutionSide.Client);
        Assert.Contains(mods, Assert.Single(logger.Errors));
        Assert.Empty(linkedRegistry.GetMods());

        File.Delete(mods); // Unlink the dangling path itself, not its missing target.
        Directory.CreateDirectory(mods);
        logger.Errors.Clear();
        string dangling = Path.Combine(mods, "dangling");
        Directory.CreateSymbolicLink(dangling, external + "-missing");
        string linkedMetadata = Directory.CreateDirectory(Path.Combine(mods, "linked-metadata")).FullName;
        string target = Path.Combine(external, "mod_info.json");
        File.WriteAllText(target, """{"id":"linked-metadata"}""");
        File.CreateSymbolicLink(Path.Combine(linkedMetadata, "mod_info.json"), target);
        string linkedSide = tree.WriteMod("linked-side", """{"id":"linked-side","contentOnly":true}""");
        Directory.CreateSymbolicLink(Path.Combine(linkedSide, "Client"), external);
        string wrongEntry = tree.WriteMod("wrong-entry", """{"id":"wrong-entry","contentOnly":true}""");
        Directory.CreateDirectory(Path.Combine(wrongEntry, "Shared", "main.lua"));
        var registry = tree.CreateRegistry(logger);
        await registry.Scan(ExecutionSide.Client);
        Assert.Empty(registry.GetMods());
        Assert.Equal(4, logger.Errors.Count);
        foreach (string path in new[] { dangling, linkedMetadata, linkedSide, wrongEntry })
            Assert.Contains(logger.Errors, error => error.Contains(path));
    }

    [Fact]
    public async Task Discovery_LinkedModsRootKeepsLegacyPathButRejectsCookedAndFlaggedPackages()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var tree = new ModTree();
        string mods = Path.Combine(tree.Root, "Mods");
        string legacy = tree.WriteMod("dev", """{"id":"dev"}""");
        string shared = Directory.CreateDirectory(Path.Combine(legacy, "Shared")).FullName;
        File.WriteAllText(Path.Combine(shared, "main.lua"), "return {}");
        string cooked = tree.WriteMod("cooked", """{"id":"cooked"}""");
        Directory.CreateDirectory(Path.Combine(cooked, "Content", "Server"));
        string flagged = tree.WriteMod("art", """{"id":"art","contentOnly":true}""");
        string oversized = tree.WriteMod("oversized", """{"id":"oversized"}""" + new string(' ', ContentCaptureFiles.MaximumMetadataBytes));
        string source = Path.Combine(tree.Root, "LinkedModsSource");
        Directory.Move(mods, source);
        Directory.CreateSymbolicLink(mods, source);
        var registry = tree.CreateRegistry();
        await registry.Scan(ExecutionSide.Server);

        Assert.Equal(legacy, Assert.Single(registry.GetMods(), mod => mod.MetaData.Id == "dev").Directory);
        foreach (var discovery in new[] { registry.StartupDiscovery, registry.Discover(AssetTarget.Server) })
        {
            Assert.Equal(["art", "cooked", "dev"], discovery.InstalledOwnerIds);
            Assert.Equal([cooked, legacy], discovery.ScriptedMods.Select(mod => mod.Directory));
            Assert.Empty(discovery.Candidates);
            Assert.Equal(3, discovery.Diagnostics.Count);
            Assert.Contains(discovery.Diagnostics, item => item.Contains(cooked) && item.Contains("Cooked Content"));
            Assert.Contains(discovery.Diagnostics, item => item.Contains(flagged) && item.Contains("contentOnly"));
            Assert.Contains(discovery.Diagnostics, item => item.Contains(oversized) && item.Contains("exceeds"));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Discovery_KeepsKnownLinkedOwnersButOnlyUnflaggedLinksCanRunLua(bool contentOnly, bool cooked)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var tree = new ModTree();
        string source = Directory.CreateDirectory(Path.Combine(tree.Root, "development-mod")).FullName;
        File.WriteAllText(Path.Combine(source, "mod_info.json"), contentOnly
            ? """{"id":"dev","contentOnly":true}"""
            : """{"id":"dev"}""");
        if (!contentOnly)
        {
            string shared = Directory.CreateDirectory(Path.Combine(source, "Shared")).FullName;
            File.WriteAllText(Path.Combine(shared, "main.lua"), "return {}");
        }
        if (cooked)
            Directory.CreateDirectory(Path.Combine(source, "Content", "Server"));
        string mods = Directory.CreateDirectory(Path.Combine(tree.Root, "Mods")).FullName;
        string link = Path.Combine(mods, "dev-link");
        Directory.CreateSymbolicLink(link, source);
        var logger = new RegistryLogger();
        var registry = tree.CreateRegistry(logger);

        await registry.Scan(ExecutionSide.Server);

        if (contentOnly) Assert.Empty(registry.GetMods());
        else Assert.Equal(link, Assert.Single(registry.GetMods()).Directory);
        foreach (var observation in new[] { registry.StartupDiscovery, registry.Discover(AssetTarget.Server) })
        {
            Assert.Equal(["dev"], observation.InstalledOwnerIds);
            Assert.Empty(observation.Candidates);
            if (!contentOnly && !cooked) Assert.Empty(observation.Diagnostics);
            else Assert.Contains(link, Assert.Single(observation.Diagnostics));
        }
        Assert.Equal(registry.StartupDiscovery.Diagnostics, logger.Errors);
    }

    [Fact]
    public async Task Discovery_RejectsFifoMetadataEvenThroughLegacyDirectoryLink()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var tree = new ModTree();
        string source = Directory.CreateDirectory(Path.Combine(tree.Root, "development-mod")).FullName;
        string metadata = Path.Combine(source, "mod_info.json");
        var mkfifo = new System.Diagnostics.ProcessStartInfo("mkfifo") { UseShellExecute = false };
        mkfifo.ArgumentList.Add(metadata);
        using var process = System.Diagnostics.Process.Start(mkfifo)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        string mods = Directory.CreateDirectory(Path.Combine(tree.Root, "Mods")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(mods, "dev-link"), source);
        var registry = tree.CreateRegistry();

        var result = registry.Discover(AssetTarget.Server);

        Assert.Empty(result.ScriptedMods);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.InstalledOwnerIds);
        Assert.Contains("ordinary Content file", Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void Discovery_LegacyMetadataModeKeepsBoundedLuaButNeverOffersCookedCandidates()
    {
        using var tree = new ModTree();
        tree.WriteMod("plain", """{"id":"plain"}""");
        string art = tree.WriteMod("art", """{"id":"art","contentOnly":true}""");
        string oversized = tree.WriteMod("oversized", """{"id":"oversized"}""" + new string(' ', ContentCaptureFiles.MaximumMetadataBytes));
        string cooked = tree.WriteMod("cooked", """{"id":"cooked"}""");
        Directory.CreateDirectory(Path.Combine(cooked, "Content", "Server"));

        var result = ModsRegistry.DiscoverAt(Path.Combine(tree.Root, "Mods"), AssetTarget.Server, default, legacyMetadataOnly: true);

        Assert.Equal(["cooked", "plain"], result.ScriptedMods.Select(mod => mod.MetaData.Id));
        Assert.Equal(["art", "cooked", "plain"], result.InstalledOwnerIds);
        Assert.Empty(result.Candidates);
        Assert.Equal(3, result.Diagnostics.Count);
        Assert.Contains(result.Diagnostics, message => message.Contains(art) && message.Contains("contentOnly"));
        Assert.Contains(result.Diagnostics, message => message.Contains(cooked) && message.Contains("Cooked Content"));
        Assert.Contains(result.Diagnostics, message => message.Contains(oversized) && message.Contains("exceeds"));
    }

    [Fact]
    public async Task Discovery_DoesNotInspectScriptedModsEntryPaths()
    {
        using var tree = new ModTree();
        string legacy = tree.WriteMod("legacy", """{"id":"legacy"}""");
        Directory.CreateDirectory(Path.Combine(legacy, "Client", "main.lua"));
        if (OperatingSystem.IsLinux())
            Directory.CreateSymbolicLink(Path.Combine(legacy, "Shared"), Path.Combine(tree.Root, "missing"));
        var registry = tree.CreateRegistry();

        await registry.Scan(ExecutionSide.Server);

        Assert.Equal("legacy", Assert.Single(registry.GetMods()).MetaData.Id);
        Assert.Empty(registry.StartupDiscovery.Diagnostics);
        var refresh = registry.Discover(AssetTarget.Server);
        Assert.Empty(refresh.Diagnostics);
        Assert.Equal("legacy", Assert.Single(refresh.Candidates).MetaData.Id);
    }

    [Fact]
    public async Task Startup_ReportsAllInstalledOwnersRegardlessOfSideOrCookedState()
    {
        using var tree = new ModTree();
        string wrongSide = tree.WriteMod("foreign", """{"id":"art","contentOnly":true}""");
        Directory.CreateDirectory(Path.Combine(wrongSide, "Content", "Client"));
        string broken = tree.WriteMod("broken", """{"id":"Broken"}""");
        string server = Directory.CreateDirectory(Path.Combine(broken, "Content", "Server")).FullName;
        File.WriteAllText(Path.Combine(server, "overrides.json"), "not json");
        tree.WriteMod("no-cooked", """{"id":"no-cooked"}""");
        var registry = tree.CreateRegistry();
        await registry.Scan(ExecutionSide.Server);

        var startup = registry.StartupDiscovery;
        Assert.Equal(["Broken", "art", "no-cooked"], startup.InstalledOwnerIds);
        Assert.Equal(startup.InstalledOwnerIds, startup.Candidates.Select(mod => mod.MetaData.Id));
        Assert.True(startup.Candidates[1].MetaData.ContentOnly);
        Assert.Empty(startup.Diagnostics);
        Assert.Equal(["Broken", "no-cooked"], registry.GetMods().Select(mod => mod.MetaData.Id));
    }

    [Fact]
    public async Task Discovery_DuplicateIdsAreFatalAndDoNotPublishEitherFolder()
    {
        using var tree = new ModTree();
        string z = tree.WriteMod("z-script", """{"id":"dup"}""");
        string a = tree.WriteMod("a-script", """{"id":"dup"}""");
        string m = tree.WriteMod("m-art", """{"id":"dup","contentOnly":true}""");
        tree.WriteMod("valid", """{"id":"valid"}""");
        var registry = tree.CreateRegistry();

        var error = await Assert.ThrowsAsync<ModDependencyException>(async () => await registry.Scan(ExecutionSide.Server));

        Assert.Throws<InvalidOperationException>(() => registry.GetMods());
        Assert.Contains("Duplicate mod id 'dup'", error.Message);
        Assert.Contains("Server", error.Message);
        Assert.Contains($"'{a}', '{m}', '{z}'", error.Message);
        Directory.Delete(a, recursive: true);
        Directory.Delete(m, recursive: true);
        await registry.Scan(ExecutionSide.Server);
        Assert.Equal(["dup", "valid"], registry.GetMods().Select(mod => mod.MetaData.Id));
    }

    [Theory]
    [InlineData("\"dependencies\":{},\"depen\\u0064encies\":{}", "dependencies", "Duplicate")]
    [InlineData("\"dependencies\":{\"server\":[],\"server\":[]}", "dependencies.server", "Duplicate")]
    [InlineData("\"dependencies\":{\"shared\":[{\"id\":\"first\",\"id\":\"second\"}]}", "dependencies.shared[0].id", "Duplicate")]
    [InlineData("\"dependencies\":{\"client\":[{\"id\":\"first\",\"optional\":false,\"optional\":true}]}", "dependencies.client[0].optional", "Duplicate")]
    [InlineData("\"dependencies\":null", "dependencies", "JSON object")]
    [InlineData("\"dependencies\":{\"server\":[{\"id\":\"provider\",\"minVersion\":7}]}", "dependencies.server[0]", "minVersion")]
    [InlineData("\"dependencies\":{\"Shared\":[]}", "dependencies.Shared", "scope")]
    [InlineData("\"dependencies\":{\"client\":[{\"id\":\"provider\",\"Optional\":true}]}", "dependencies.client[0]", "Optional")]
    public async Task Discovery_RejectsMalformedDependencyContractWithoutPublishing(string fields, string location, string reason)
    {
        using var tree = new ModTree();
        string path = tree.WriteMod("broken-folder", $$"""{ {{fields}}, "id":"broken" }""");
        tree.WriteMod("valid", """{"id":"valid"}""");
        var registry = tree.CreateRegistry();

        var error = await Assert.ThrowsAsync<ModDependencyException>(async () => await registry.Scan(ExecutionSide.Server));

        Assert.Contains(path, error.Message);
        Assert.Contains("broken", error.Message);
        Assert.Contains(location, error.Message);
        Assert.Contains(reason, error.Message);
        Assert.Throws<InvalidOperationException>(() => registry.GetMods());
    }

    [Theory]
    [InlineData("\"dependencies\":{\"shared\":[?]}", "dependencies.shared")]
    [InlineData("\"contentOnly\":1,\"dependencies\":null", "dependencies")]
    [InlineData("\"dependencies\":null", "dependencies", " {}")]
    [InlineData("\"dependencies\":{},\"dependencies\":{}", "dependencies", " {}")]
    [InlineData("\"dependencies\":{},\"dependencies\":{},\"legacy\" {}", "Duplicate dependency property")]
    [InlineData("\"dependencies\":null,\"legacy\" {}", "must be a JSON object")]
    [InlineData("\"extension\":{\"dependencies\":{}},\"depen\\u0064encies\":{/* comment */\"unknown\":[]},\"legacy\" {}", "dependencies.unknown")]
    [InlineData("\"dependencies\":{\"server\":[{\"id\":\"broken-id\"}]},\"legacy\":?", "cannot depend on itself")]
    public async Task Discovery_DoesNotDowngradeExplicitDependencyErrorsToLegacyWarnings(string fields, string location, string tail = "")
    {
        using var tree = new ModTree();
        string path = tree.WriteMod("broken-folder", $$"""{ "id":"broken-id", {{fields}} }{{tail}}""");
        tree.WriteMod("valid", """{"id":"valid"}""");
        var registry = tree.CreateRegistry();

        var error = await Assert.ThrowsAsync<ModDependencyException>(async () => await registry.Scan(ExecutionSide.Server));

        Assert.Contains(path, error.Message);
        Assert.Contains("broken-id", error.Message);
        Assert.Contains(location, error.Message);
        Assert.Throws<InvalidOperationException>(() => registry.GetMods());
    }

    [Theory]
    [InlineData("{\"id\":\"broken-id\",\"dependencies\" {}}")]
    [InlineData("{\"id\":\"broken-id\",\r\n\"depen\\u0064encies\"\r\n{}}")]
    [InlineData("{\"id\":\"broken-id\",/* root member */'dependencies' {}}")]
    [InlineData("{\"id\":\"broken-id\",dependencies {}}")]
    [InlineData("{\"id\":\"broken-id\",\"dependencies\"")]
    public async Task Discovery_RejectsMalformedRootDependencyKeyBeforeReaderEmitsIt(string metadata)
    {
        using var tree = new ModTree();
        string path = tree.WriteMod("broken-folder", metadata);
        tree.WriteMod("valid", """{"id":"valid"}""");
        var registry = tree.CreateRegistry();

        var error = await Assert.ThrowsAsync<ModDependencyException>(async () => await registry.Scan(ExecutionSide.Server));

        Assert.Contains(path, error.Message);
        Assert.Contains("broken-id", error.Message);
        Assert.Contains("dependencies", error.Message);
        Assert.Throws<InvalidOperationException>(() => registry.GetMods());
    }

    [Theory]
    [InlineData("{\"id\":\"broken\",\"name\" {}}")]
    [InlineData("{\"id\":\"broken\",\"Dependencies\" {}}")]
    [InlineData("{\"id\":\"broken\",\"legacy\":{\"dependencies\" {}}}")]
    [InlineData("{\"id\":\"broken\",\"name\":\"dependencies\",\"legacy\" {}}")]
    [InlineData("{\"id\":\"broken\",\"dependencies\":{},\"legacy\" {}}")]
    public async Task Discovery_KeepsUnrelatedMalformedMembersAsLegacyWarnings(string metadata)
    {
        using var tree = new ModTree();
        string path = tree.WriteMod("broken-folder", metadata);
        tree.WriteMod("valid", """{"id":"valid"}""");
        var logger = new RegistryLogger();
        var registry = tree.CreateRegistry(logger);

        await registry.Scan(ExecutionSide.Server);

        Assert.Equal("valid", Assert.Single(registry.GetMods()).MetaData.Id);
        Assert.Contains(path, Assert.Single(logger.Errors));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Discovery_DoesNotExpandLongAncestorNamesForEachProperty(bool inDependencies)
    {
        using var tree = new ModTree();
        string section = inDependencies ? "dependencies" : "legacy";
        tree.WriteMod("mod", """{"id":"mod"}""");
        var registry = tree.CreateRegistry();
        _ = registry.Discover(AssetTarget.Server); // Warm discovery before counting thread allocations.
        string ancestor = new('a', 4096);
        string children = string.Join(',', Enumerable.Range(0, 768).Select(id => $"\"k{id}\":0"));
        tree.WriteMod("mod", $$"""{ "id":"mod", "{{section}}": { "{{ancestor}}": { {{children}} } } }""");

        long before = GC.GetAllocatedBytesForCurrentThread();
        if (inDependencies)
            Assert.Contains("not a recognized scope", Assert.Throws<ModDependencyException>(() => registry.Discover(AssetTarget.Server)).Message);
        else
            Assert.Equal("mod", Assert.Single(registry.Discover(AssetTarget.Server).ScriptedMods).MetaData.Id);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 4 * 1024 * 1024, $"A bounded metadata read allocated {allocated:N0} bytes.");
    }

    [Fact]
    public async Task Discovery_PreservesUnrelatedLegacyDuplicateFieldsAndExactRootKey()
    {
        using var tree = new ModTree();
        tree.WriteMod("legacy", """
            {"id":"old","id":"final","name":"old","name":"final-name","Dependencies":null,
             "custom":{"id":1,"id":2},"dependencies":{"shared":[{"id":"a"},{"id":"b"}]}}
            """);
        var logger = new RegistryLogger();
        var registry = tree.CreateRegistry(logger);

        await registry.Scan(ExecutionSide.Server);

        var definition = Assert.Single(registry.GetMods());
        Assert.Equal(("final", "final-name"), (definition.MetaData.Id, definition.MetaData.Name));
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task Discovery_RereadsInventoryWithoutMutatingPublishedStartupOrLua()
    {
        using var tree = new ModTree();
        string old = tree.WriteMod("old", """{"id":"old"}""");
        var registry = tree.CreateRegistry();
        await registry.Scan(ExecutionSide.Server);
        var startup = registry.StartupDiscovery;
        var active = registry.GetMods();
        Directory.Delete(old, recursive: true);
        string added = tree.WriteMod("new", """{"id":"new","contentOnly":true}""");

        var refreshed = registry.Discover(AssetTarget.Server);
        Assert.Equal(["new"], refreshed.InstalledOwnerIds);
        Assert.Equal("new", Assert.Single(refreshed.Candidates).MetaData.Id);
        Assert.Empty(refreshed.Diagnostics);
        Assert.Same(startup, registry.StartupDiscovery);
        Assert.Equal(["old"], startup.InstalledOwnerIds);
        Assert.Equal("old", Assert.Single(active).MetaData.Id);
        Assert.Equal(active, registry.GetMods());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await registry.Scan(ExecutionSide.Server));
        Assert.ThrowsAny<OperationCanceledException>(() => registry.Discover(AssetTarget.Server, new CancellationToken(canceled: true)));

        File.Delete(Path.Combine(added, "mod_info.json"));
        Assert.Contains("Missing mod_info.json", Assert.Single(registry.Discover(AssetTarget.Server).Diagnostics));
        Directory.Delete(added);
        var removed = registry.Discover(AssetTarget.Server);
        Assert.Empty(removed.Diagnostics);
        Assert.Empty(removed.InstalledOwnerIds);
        Assert.Empty(removed.Candidates);
    }

    private sealed class RegistryLogger : ILogger<IModsRegistry>
    {
        public List<string> Errors { get; } = [];
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) Errors.Add(formatter(state, error));
        }
    }

    private sealed class ModTree : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("karpik-mod-discovery-").FullName;

        public string WriteMod(string folder, string metadata)
        {
            string directory = Directory.CreateDirectory(Path.Combine(Root, "Mods", folder)).FullName;
            File.WriteAllText(Path.Combine(directory, "mod_info.json"), metadata);
            return directory;
        }

        public ModsRegistry CreateRegistry(ILogger<IModsRegistry>? logger = null) =>
            FromRoot(() => new ModsRegistry(assetsManager: null!, logger ?? NullLogger<IModsRegistry>.Instance));

        public T FromRoot<T>(Func<T> create)
        {
            string previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = Root;
                return create();
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
