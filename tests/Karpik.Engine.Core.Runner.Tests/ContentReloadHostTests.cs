using Autofac;
using System.Diagnostics;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Content.Transport;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;
using Xunit;
using RuntimeOutcome = Karpik.Content.Runtime.ContentReloadOutcome;
using WireOutcome = Karpik.Content.Transport.ContentReloadOutcome;

public sealed class ContentReloadHostTests
{
    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, false)]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, true)]
    public async Task RealHostProcess_AppliesRepeatedBuiltinReloadsAndShutsDown(Side side, bool launcherManaged)
    {
        using var tree = new HostTree(side);
        using var initial = tree.Build(1);
        string pipe = "content-host-" + Guid.NewGuid().ToString("N");
        using var launcher = launcherManaged ? new IpcServer(pipe) : null;
        Task? connection = launcher?.WaitForConnectionAsync();
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = tree.Root, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        info.ArgumentList.Add(typeof(Karpik.Content.Reload.HeadlessHost.HostMarker).Assembly.Location);
        info.ArgumentList.Add(side.ToString());
        if (side == Side.Client) info.ArgumentList.Add("hold");
        info.ArgumentList.Add("--content-reload-instance"); info.ArgumentList.Add("test-host");
        if (launcherManaged) { info.ArgumentList.Add("--pipe-name"); info.ArgumentList.Add(pipe); }
        using var child = Process.Start(info)!;
        Task<string> errors = child.StandardError.ReadToEndAsync();
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var observed = new TaskCompletionSource[4];
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        for (int i = 0; i < observed.Length; i++) observed[i] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task output = Task.Run(async () =>
        {
            while (await child.StandardOutput.ReadLineAsync() is { } line)
            {
                if (lines.Count < 100 && !line.StartsWith("[ProfilerMark]", StringComparison.Ordinal)) lines.Enqueue(line);
                if (line.StartsWith("observed:", StringComparison.Ordinal) && int.TryParse(line.AsSpan(9), out int value) && value is > 0 and < 4)
                    observed[value].TrySetResult();
                if (line == "holding") holding.TrySetResult();
            }
        });
        try
        {
            if (connection != null) await connection.WaitAsync(TimeSpan.FromSeconds(15));
            await observed[1].Task.WaitAsync(TimeSpan.FromSeconds(15));
            for (int value = 2; value <= 3; value++)
            {
                using var next = tree.Build(value);
                Task<ContentReloadResponse> delivery = tree.Deliver(next.GenerationId!);
                if (side == Side.Client && value == 3)
                {
                    await holding.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    string claims = Path.Combine(next.PublishedGeneration!.RootPath, ".claims");
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    while (Directory.GetFiles(claims).Length < 2) await Task.Delay(1, deadline.Token);
                    Assert.False(delivery.IsCompleted); // Accepted while the previous simulation frame still owns its buffer.
                    await child.StandardInput.WriteLineAsync("release");
                }
                var response = await delivery;
                Assert.Equal(WireOutcome.Applied, response.Outcome);
                await observed[value].Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            if (launcher != null) await launcher.SendShutdownRequestAsync(TimeSpan.FromSeconds(5));
            else await child.StandardInput.WriteLineAsync("stop");
            child.StandardInput.Close();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await output;
            Assert.True(child.ExitCode == 0, await errors);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("Headless host failed. Output: " + string.Join("\n", lines), error);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            await output;
            await errors;
        }
    }

    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, false)]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, true)]
    public async Task OptIn_DeliversThroughInjectedSimulationBoundary(Side side, bool staticMode)
    {
        using var tree = new HostTree(side);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode, enabled: true);
        try
        {
            Assert.True(observation.SharedContracts);
            using var next = tree.Build(2);
            Task<ContentReloadResponse> delivered = tree.Deliver(next.GenerationId!);
            await PumpUntilAsync(runner, delivered);
            Assert.Equal(WireOutcome.Applied, (await delivered).Outcome);
            Assert.Equal(1, observation.ChangedAtBegin);
            if (side == Side.Server) Assert.Equal(1, observation.ChangedAtFixed);
            Assert.Equal(ContentAssetChangeKind.Updated, observation.LastKind);
            Assert.Equal(tree.AssetId, observation.LastId);
            runner.Run(Application.TICK_DT);
            Assert.Equal(0, observation.LastCount);
        }
        finally { await runner.DestroyAsync(); }
        Assert.Equal(RuntimeOutcome.Failed, observation.ShutdownRequestOutcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultRuntime_ProvidesEmptyInjectedSourceWithoutOpeningEndpoint(bool staticMode)
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode, enabled: false);
        try
        {
            runner.Run(Application.TICK_DT);
            Assert.Equal(0, observation.LastCount);
            Assert.Equal(0UL, observation.Sequence);
            await using var endpoint = new ContentReloadEndpoint(tree.Address,
                (request, _) => Task.FromResult(new ContentReloadResponse(1, request.RequestId, WireOutcome.Applied)));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MismatchedSideFailsBeforeEndpointBinding(bool staticMode)
    {
        using var tree = new HostTree(Side.Client);
        using var initial = tree.Build(1);
        var error = Assert.ThrowsAny<Exception>(() => tree.Setup(new Observation(), staticMode, enabled: true, runtimeSide: Side.Server));
        Assert.Contains("target does not match", error.ToString());
        using var endpoint = new AsyncDisposalAdapter(new ContentReloadEndpoint(tree.Address,
            (request, _) => Task.FromResult(new ContentReloadResponse(1, request.RequestId, WireOutcome.Applied))));
    }

    [Fact]
    public async Task DeliveredGenerationIdentityIsValidatedBeforePreparation()
    {
        using var tree = new HostTree(Side.Client);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: true, enabled: true);
        try
        {
            using var wrong = tree.Build(2, AssetTarget.Server);
            Assert.Equal(WireOutcome.Failed, (await tree.Deliver(wrong.GenerationId!)).Outcome);
            Assert.Equal(0UL, observation.Source!.Sequence);
            using var correct = tree.Build(3);
            Task<ContentReloadResponse> delivery = tree.Deliver(correct.GenerationId!);
            await PumpUntilAsync(runner, delivery);
            Assert.Equal(WireOutcome.Applied, (await delivery).Outcome);
            Assert.Equal(1, observation.ChangedAtBegin);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public void LegacyUnknownIdentityRequiresRebuildBeforeOpeningEndpoint()
    {
        using var tree = new HostTree(Side.Server);
        using (tree.Build(1)) { }
        File.Delete(Path.Combine(tree.FileSystem.ContentPath, ".karpik-current-generation"));
        Directory.Delete(Path.Combine(tree.FileSystem.ContentPath, ".generations"), recursive: true);
        var error = Assert.ThrowsAny<Exception>(() => tree.Setup(new Observation(), staticMode: true, enabled: true));
        Assert.Contains("Rebuild Content", error.ToString());
        using var endpoint = new AsyncDisposalAdapter(new ContentReloadEndpoint(tree.Address,
            (request, _) => Task.FromResult(new ContentReloadResponse(1, request.RequestId, WireOutcome.Applied))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InjectedFrameBoundaryAllocatesNeitherSteadyStateNorPublication(bool staticMode)
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode, enabled: true);
        try
        {
            for (int i = 0; i < 100; i++) runner.RunMainThreadFrameBegin();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) runner.RunMainThreadFrameBegin();
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            using var next = tree.Build(2);
            Task<ContentReloadResponse> delivery = tree.Deliver(next.GenerationId!);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (observation.Source!.Sequence == 0)
            {
                before = GC.GetAllocatedBytesForCurrentThread();
                runner.RunMainThreadFrameBegin();
                Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
                if (observation.Source.Sequence == 0) await Task.Delay(1, deadline.Token);
            }
            Assert.Equal(WireOutcome.Applied, (await delivery).Outcome);
            Assert.Single(observation.Source.Changes.ToArray());
            runner.RunGameplayFrame(Application.TICK_DT);
            Assert.Equal(1, observation.ChangedAtBegin);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ShutdownSettlesAcceptedUnpublishedDeliveryBeforeRegistryTeardown()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: true, enabled: true);
        using var next = tree.Build(2);
        Task<ContentReloadResponse> delivery = tree.Deliver(next.GenerationId!);
        string claims = Path.Combine(next.PublishedGeneration!.RootPath, ".claims");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Directory.GetFiles(claims).Length < 2) await Task.Delay(1, deadline.Token);
        Assert.False(delivery.IsCompleted);
        await runner.DestroyAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains((await delivery).Outcome, new[] { WireOutcome.Failed, WireOutcome.Unknown });
        Assert.Equal(0UL, observation.Source!.Sequence);
        using var endpoint = new AsyncDisposalAdapter(new ContentReloadEndpoint(tree.Address,
            (request, _) => Task.FromResult(new ContentReloadResponse(1, request.RequestId, WireOutcome.Applied))));
    }

    private static async Task PumpUntilAsync(EngineRunner runner, Task task)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!task.IsCompleted)
        {
            deadline.Token.ThrowIfCancellationRequested();
            runner.Run(Application.TICK_DT);
            await Task.Delay(1, deadline.Token);
        }
    }

    private sealed class Observation
    {
        public bool SharedContracts;
        public int ChangedAtBegin;
        public int ChangedAtFixed;
        public int LastCount;
        public ulong Sequence;
        public ContentAssetChangeKind LastKind;
        public AssetId LastId;
        public RuntimeOutcome? ShutdownRequestOutcome;
        public IContentChangeSource? Source;
    }

    private sealed class ObserverSystem(IContentChangeSource changes, Observation observation,
        ContentRegistry? registry = null, IContentRegistry? readRegistry = null, IContentUpdateRegistry? updateRegistry = null,
        ContentReloadCoordinator? coordinator = null, IContentReloadLifetime? lifetime = null, FileContentStore? store = null)
        : ISystemInit, ISystemBegin, ISystemFixedUpdate, ISystemAsyncDestroy
    {
        public void Init()
        {
            observation.Source = changes;
            observation.SharedContracts = coordinator != null && ReferenceEquals(coordinator, changes) &&
                ReferenceEquals(coordinator, lifetime) && ReferenceEquals(registry, readRegistry) && ReferenceEquals(registry, updateRegistry);
        }
        public void Begin()
        {
            observation.LastCount = changes.Changes.Length;
            observation.Sequence = changes.Sequence;
            if (changes.Changes.Length == 0) return;
            observation.ChangedAtBegin++;
            observation.LastKind = changes.Changes[0].Kind;
            observation.LastId = changes.Changes[0].AssetId;
        }
        public void FixedUpdate() { if (changes.Changes.Length != 0) observation.ChangedAtFixed++; }
        public ValueTask DestroyAsync()
        {
            if (coordinator != null)
                observation.ShutdownRequestOutcome = coordinator.EnqueueAsync(store!.CapturedManifest!, store).GetAwaiter().GetResult().Outcome;
            return ValueTask.CompletedTask;
        }
    }

    [Module(ModuleScope.Engine)]
    private sealed class HostServices(HostTree tree, Observation observation) : IModuleInstaller
    {
        public string Name => nameof(HostServices);
        public void OnRegisterServices(ContainerBuilder builder)
        {
            builder.RegisterInstance(tree.FileSystem).As<IFileSystem>();
            builder.RegisterInstance(observation);
        }
    }

    [Module(ModuleScope.Simulation)]
    private sealed class ObserverModule : IModuleInstaller, IModule
    {
        public string Name => nameof(ObserverModule);
        public IModule CreateModule() => this;
        public void Add(ISystemRegistry systems) => systems.Add<ObserverSystem>();
        public void OnRegisterServices(ContainerBuilder builder) => builder.Register(context => new ObserverSystem(
            context.Resolve<IContentChangeSource>(), context.Resolve<Observation>(), context.ResolveOptional<ContentRegistry>(),
            context.ResolveOptional<IContentRegistry>(), context.ResolveOptional<IContentUpdateRegistry>(), context.ResolveOptional<ContentReloadCoordinator>(),
            context.Resolve<IContentReloadLifetime>(), context.ResolveOptional<FileContentStore>()));
    }

    private sealed class Composition(HostTree tree, Observation observation) : IStaticRuntimeComposition
    {
        public void RegisterModules(IStaticModuleRegistry registry)
        { registry.Add(new HostServices(tree, observation)); registry.Add(new ObserverModule()); }
        public void RegisterServices(IStaticServiceRegistry registry) { }
        public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry) { }
    }

    private sealed class HostTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "karpik-reload-host-" + Guid.NewGuid().ToString("N"));
        public AssetId AssetId { get; } = new(Guid.NewGuid());
        private readonly Side _side;
        public IFileSystem FileSystem { get; }
        public ContentReloadAddress Address { get; }
        private string Source => Path.Combine(Root, "Source");
        public HostTree(Side side)
        {
            _side = side;
            FileSystem = new HostFileSystem(Root);
            Directory.CreateDirectory(Source);
            Address = ContentReloadAddress.Create(FileSystem.ContentPath, "test-host", "game",
                side == Side.Client ? ContentReloadTarget.Client : ContentReloadTarget.Server);
            File.WriteAllText(Path.Combine(Source, "a.json.meta"), System.Text.Json.JsonSerializer.Serialize(new
            { schemaVersion = 1, assetId = AssetId.ToCanonicalString(), declaredType = "raw-json", logicalName = "game/a", importSettings = new { }, dependencies = Array.Empty<string>() }));
        }
        public ContentBuildResult Build(int value, AssetTarget? target = null)
        {
            File.WriteAllText(Path.Combine(Source, "a.json"), "{\"value\":" + value + "}");
            var result = new ContentBuildCoordinator().Build(new ContentBuildOptions
            { SourceRoot = Source, OutputRoot = FileSystem.ContentPath, Namespace = "game", Target = target ?? (_side == Side.Client ? AssetTarget.Client : AssetTarget.Server), RetainGeneration = true });
            Assert.True(result.Success, string.Join(";", result.Diagnostics.Select(d => d.Message)));
            return result;
        }
        public EngineRunner Setup(Observation observation, bool staticMode, bool enabled, Side? runtimeSide = null)
        {
            var runner = new EngineRunner();
            if (staticMode) runner.RegisterStaticComposition(new Composition(this, observation));
            else { runner.RegisterModule(new HostServices(this, observation)); runner.RegisterModule(new ObserverModule()); }
            if (enabled) runner.EnableContentReload("test-host");
            var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
            runner.Setup(new Application(runtimeSide ?? _side), scheduler);
            scheduler.Execute();
            return runner;
        }
        public Task<ContentReloadResponse> Deliver(string generation) => ContentReloadClient.DeliverAsync(Address,
            new ContentReloadRequest(1, Guid.NewGuid(), Address.OutputRoot, "game", Address.Target, generation), TimeSpan.FromSeconds(10));
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class HostFileSystem(string root) : IFileSystem
    {
        public string RootPath => root;
        public string ContentPath => Path.Combine(root, "Content");
        public string ModsPath => Path.Combine(root, "Mods");
        public char DirectorySeparatorChar => Path.DirectorySeparatorChar;
        public bool Exists(string path) => File.Exists(path);
        public string GetExtension(string path) => Path.GetExtension(path);
        public bool ExistsDirectory(string path) => Directory.Exists(path);
        public Stream OpenRead(string path) => File.OpenRead(path);
        public Stream OpenWrite(string path) => File.OpenWrite(path);
        public string Combine(params ReadOnlySpan<string> path) => Path.Combine(path);
        public Span<string> GetDirectories(string path) => Directory.GetDirectories(path);
        public Span<string> GetFiles(string path) => Directory.GetFiles(path);
        public Span<string> GetFiles(string path, string pattern, SearchOption option) => Directory.GetFiles(path, pattern, option);
        public string GetFileName(string path) => Path.GetFileName(path);
    }
    private sealed class AsyncDisposalAdapter(ContentReloadEndpoint endpoint) : IDisposable
    { public void Dispose() { endpoint.DisposeAsync().AsTask().GetAwaiter().GetResult(); endpoint.Completion.GetAwaiter().GetResult(); } }
}
