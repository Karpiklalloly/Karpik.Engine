using System.Diagnostics;
using System.Runtime.CompilerServices;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

public sealed partial class LuaPreparationTests
{
    private readonly ITestOutputHelper _reloadOutput;
    public LuaPreparationTests(ITestOutputHelper output) => _reloadOutput = output;

    [Theory]
    [InlineData(false, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(false, EcsUpdateSchedulerMode.Parallel)]
    [InlineData(true, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(true, EcsUpdateSchedulerMode.Parallel)]
    public async Task LuaHotReload_FileReplacementPreservesStorageAndChangesGraph(bool useStatic, EcsUpdateSchedulerMode mode)
    {
        await WithLuaFiles(ReloadSource(1, "old"), null, (_, definition) =>
        {
            using var f = new ReloadRunnerFixture(definition, useStatic, mode);
            int entity = f.Entity(10);
            entlong identity = f.World.GetEntityLong(entity);
            f.Tick();
            Assert.Equal(31, f.Position(entity)); // Native 10 -> new Temperature 30 -> next Temperature 31.
            var root = f.Installer.Bindings!.Queries;
            var oldQuery = root.Prepare(new("retained", [], [], [new("probe:Temperature", false)]), "retained");
            var oldHandle = oldQuery.Components["probe:Temperature"];
            root.CallbackScope.Enter("retained");
            PreparedComponentView oldView = oldHandle.Get(entity);
            ComponentSnapshot oldSnapshot = oldHandle.Copy(entity);
            Assert.Equal(31, oldView.ReadF32(0));
            root.CallbackScope.Exit();
            ModContainer oldOwner = f.Owner;
            oldOwner.IsEnabled = false;
            f.Write(ReloadSource(100, "new", systems: 2));
            Assert.Equal(SimulationCodeReloadStatus.Success, f.Reload().Status);
            Assert.Same(f.World, f.Installer.Bindings.World);
            Assert.Equal(identity, f.World.GetEntityLong(entity));
            Assert.Equal(1, f.World.Count);
            Assert.Equal(31, f.Position(entity)); // Preparation/commit did not execute new systems.
            Assert.False(f.Owner.IsEnabled);
            f.Tick();
            Assert.Equal(31, f.Position(entity)); // Disabled state survived the code revision.
            Assert.Throws<ObjectDisposedException>(() => root.CallbackScope.Enter("retained"));
            Assert.Throws<ObjectDisposedException>(() => oldQuery.Capture());
            Assert.Throws<ObjectDisposedException>(() => oldHandle.Get(entity));
            Assert.Throws<ObjectDisposedException>(() => oldView.ReadF32(0));
            Assert.Throws<ObjectDisposedException>(() => oldSnapshot.ReadF32(0));
            Assert.Throws<ObjectDisposedException>(() => oldOwner.Update(.02));
            f.Owner.IsEnabled = true;
            f.Tick();
            Assert.Equal(132, f.Position(entity)); // Retained dynamic 31, then two new systems use step 100.
            Assert.Equal(2, ((LuaScriptRuntime)f.Owner.Runtime).Systems.Length);
            Assert.Equal(1, f.Logger.Messages.Count(m => m == "[Mod: probe] start:old"));
            Assert.DoesNotContain("[Mod: probe] start:new", f.Logger.Messages);
            Assert.Contains("[Mod: probe] reload:new", f.Logger.Messages);
            Assert.Contains("[Mod: probe] destroy:old", f.Logger.Messages);
            Assert.Contains("[Mod: probe] unload:old", f.Logger.Messages);
            f.Write(ReloadSource(10, "empty", systems: 0));
            Assert.Equal(SimulationCodeReloadStatus.Success, f.Reload().Status);
            f.Tick();
            Assert.Equal(132, f.Position(entity)); // Removing every Lua system leaves the same native system alive.
            f.Write(ReloadSource(10, "restored", systems: 1));
            Assert.Equal(SimulationCodeReloadStatus.Success, f.Reload().Status);
            f.Tick();
            Assert.Equal(43, f.Position(entity)); // Dynamic 33 survived the zero-system revision too.
            Assert.Equal(identity, f.World.GetEntityLong(entity));
            Assert.True(f.Runner.IsApplicationRunning);
            Assert.False(f.Runner.IsCodeReloading);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("syntax")]
    [InlineData("schema")]
    [InlineData("query")]
    [InlineData("graph")]
    [InlineData("hook")]
    public async Task LuaHotReload_RealFileFailureKeepsOldWorldVersionAndCallbacks(string failure)
    {
        await WithLuaFiles(ReloadSource(1, "old"), null, (_, definition) =>
        {
            using var f = new ReloadRunnerFixture(definition, false, EcsUpdateSchedulerMode.Deterministic);
            int entity = f.Entity(10); f.Tick();
            var owner = f.Owner;
            var runtime = owner.Runtime;
            entlong identity = f.World.GetEntityLong(entity);
            f.Write(RejectedSource(failure));
            var result = f.Reload();
            Assert.Equal(SimulationCodeReloadStatus.Rejected, result.Status);
            Assert.NotNull(result.Error);
            Assert.Same(owner, f.Owner);
            Assert.Same(runtime, f.Owner.Runtime);
            Assert.Equal(31, f.Position(entity));
            Assert.Equal(identity, f.World.GetEntityLong(entity));
            Assert.DoesNotContain("[Mod: probe] destroy:old", f.Logger.Messages);
            Assert.DoesNotContain("[Mod: probe] start:new", f.Logger.Messages);
            f.Tick();
            Assert.Equal(32, f.Position(entity)); // Old step=1, with preserved dynamic state.
            Assert.True(f.Runner.IsApplicationRunning);
            Assert.False(f.Runner.IsCodeReloading);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LuaHotReload_RejectsMissingPreviouslyCapturedEntryAndKeepsLazySource()
    {
        if (!LuaSourceCapture.IsSupported) return;
        await WithLuaFiles("return { on_update = function() assert(require('late') == 'old') end }", null,
            (_, definition) =>
            {
                string shared = Path.Combine(definition.Directory, "Shared");
                string helper = Path.Combine(shared, "late.lua");
                File.WriteAllText(helper, "return 'old'");
                using var fixture = new ReloadRunnerFixture(definition, false, EcsUpdateSchedulerMode.Deterministic);
                var oldOwner = fixture.Owner;
                File.Delete(Path.Combine(shared, "main.lua"));
                File.Delete(helper);

                var result = fixture.Reload();

                Assert.Equal(SimulationCodeReloadStatus.Rejected, result.Status);
                Assert.Contains("main.lua", result.Error!.ToString());
                Assert.Same(oldOwner, fixture.Owner);
                fixture.Tick();
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task LuaHotReload_SeededSuccessRejectCancelSequencesReleaseRetiredRevisions()
    {
        WeakReference[] retired = [];
        await WithLuaFiles(ReloadSource(1, "old"), null, (_, definition) =>
        {
            using var f = new ReloadRunnerFixture(definition, false, EcsUpdateSchedulerMode.Deterministic);
            int entity = f.Entity(10); f.Tick();
            entlong identity = f.World.GetEntityLong(entity);
            var references = new List<WeakReference>();
            float temperature = 31;
            int activeStep = 1;
            for (int seed = 0; seed < 100; seed++)
            {
                var random = new Random(seed);
                for (int operation = 0; operation < 3; operation++)
                {
                    bool success = operation != 1;
                    int step = random.Next(1, 100);
                    string source = ReloadSource(step, $"seed{seed}_{operation}");
                    SimulationCodeReloadStatus expected;
                    using var cancellation = new CancellationTokenSource();
                    if (success) expected = SimulationCodeReloadStatus.Success;
                    else
                    {
                        string[] failures = ["schema", "graph", "hook", "query", "syntax", "cancel"];
                        string failure = failures[random.Next(failures.Length)];
                        expected = failure == "cancel" ? SimulationCodeReloadStatus.Cancelled : SimulationCodeReloadStatus.Rejected;
                        if (failure == "cancel") cancellation.Cancel();
                        else source = RejectedSource(failure);
                    }
                    references.AddRange(ReloadAndTrack(f, source, expected, cancellation.Token));
                    if (success) activeStep = step;
                    f.Tick();
                    Assert.Equal(temperature + activeStep, f.Position(entity));
                    temperature++;
                    Assert.Equal(identity, f.World.GetEntityLong(entity));
                    Assert.Equal(1, f.World.Count);
                    Assert.True(f.Runner.IsApplicationRunning);
                }
            }
            Assert.Equal(1, f.Logger.Messages.Count(m => m.StartsWith("[Mod: probe] start:", StringComparison.Ordinal)));
            Assert.False(f.Runner.IsCodeReloading);
            retired = references.ToArray();
            return Task.CompletedTask;
        });
        CollectRetired(retired);
        Assert.Equal(600, retired.Length); // 200 replacements: owner, VM and prepared context each.
        _reloadOutput.WriteLine("100 deterministic seeds × success/reject-or-cancel/success; 300 real host requests, 200 replacements; 600 retired owner/VM/context weak references released.");
    }

    [Fact]
    public async Task LuaHotReload_ZeroSystemCommonTicksAllocateZeroBeforeAndAfterReload()
    {
        await WithLuaFiles(ReloadSource(1, "old", systems: 0), null, (_, definition) =>
        {
            using var f = new ReloadRunnerFixture(definition, true, EcsUpdateSchedulerMode.Deterministic);
            // Native probe writes the same existing string; reserve the test logger outside measurement.
            f.Logger.Messages.EnsureCapacity(10000);
            Assert.Equal(0, CommonTickBytes(f));
            f.Write(ReloadSource(2, "new", systems: 0));
            Assert.Equal(SimulationCodeReloadStatus.Success, f.Reload().Status);
            Assert.Equal(0, CommonTickBytes(f));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public async Task LuaHotReload_ReportsMaintenanceResourcesForRepeatedRealReloads(int systems)
    {
        WeakReference[] retired = [];
        await WithLuaFiles(ReloadSource(1, "old", systems), null, (_, definition) =>
        {
            using var f = new ReloadRunnerFixture(definition, false, EcsUpdateSchedulerMode.Parallel);
            for (int i = 0; i < 16; i++) f.Entity(i + 1);
            f.Tick();
            var references = new List<WeakReference>();
            using var sampler = new ReloadResourceSampler();
            sampler.Start();
            long allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            double totalPauseBefore = f.Runner.CodeReloadPausedSeconds;
            long elapsedStart = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < 20; iteration++)
            {
                references.AddRange(ReloadAndTrack(f, ReloadSource(1 + iteration, $"probe{iteration}", systems), SimulationCodeReloadStatus.Success));
                var metrics = f.Runner.LastCodeReloadMetrics;
                Assert.InRange(metrics.PauseSeconds, 0, double.MaxValue);
                Assert.InRange(metrics.PreparationSeconds, 0, metrics.PauseSeconds);
                Assert.InRange(metrics.CommitSeconds, 0, metrics.PauseSeconds);
                Assert.InRange(metrics.RetirementSeconds, 0, metrics.PauseSeconds);
                _reloadOutput.WriteLine($"N={systems}; entities=16; iteration={iteration}; pause_s={metrics.PauseSeconds:F6}; prepare_s={metrics.PreparationSeconds:F6}; commit_s={metrics.CommitSeconds:F6}; retire_s={metrics.RetirementSeconds:F6}; reload_process_alloc_B={metrics.ProcessAllocatedBytes}");
                f.Tick();
            }
            double elapsed = Stopwatch.GetElapsedTime(elapsedStart).TotalSeconds;
            long allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            sampler.Stop();
            Assert.False(f.Runner.IsCodeReloading);
            Assert.Equal(16, f.World.Count);
            _reloadOutput.WriteLine($"N={systems}; entities=16; reloads=20; wall_s={elapsed:F6}; total_pause_s={f.Runner.CodeReloadPausedSeconds - totalPauseBefore:F6}; process_alloc_B={allocationAfter-allocationBefore}; sample_peak_managed_B={sampler.ManagedPeak}; sample_peak_working_B={sampler.WorkingSetPeak}; sample_peak_threads={sampler.ThreadPeak}; samples={sampler.Samples}; sampler_alloc_B={sampler.AllocatedBytes}");
            _reloadOutput.WriteLine("Resource peaks are sampled lower bounds at 2ms; whole-process allocation/threads include CLR, test runner and sampler overhead. Reload process allocation includes off-thread and sampler activity. Lua remains experimental; these reload costs are separate from warmed common tick 0B.");
            retired = references.ToArray();
            return Task.CompletedTask;
        });
        CollectRetired(retired);
    }

    private static string ReloadSource(int step, string revision, int systems = 1)
    {
        var declarations = new string[systems];
        for (int i = 0; i < systems; i++)
            declarations[i] = $$"""
              { id = 'run{{i}}', phase = 'update', callback = update,
                queries = { q = { inc = { ['probe:position'] = {readonly = false} },
                  opt = { ['probe:Temperature'] = {readonly = false} } } } }
              """;
        return $$"""
            local reloaded = false
            local function update(dt, ctx)
                local q = ctx.queries.q
                local positions = q.components['probe:position']
                local temperatures = q.components['probe:Temperature']
                for entity in q:entities() do
                    local p = positions:get(entity)
                    local t
                    if temperatures:has(entity) then t = temperatures:get(entity)
                    else t = temperatures:add(entity); t.Value = p.X + 20 end
                    p.X = t.Value + {{step}}
                    t.Value = t.Value + 1
                end
            end
            return {
                components = {Temperature = {version = 1, fields = {Value = 'f32'} } },
                on_load = function() print('load:{{revision}}') end,
                on_start = function() print('start:{{revision}}') end,
                on_reload = function() reloaded = true; print('reload:{{revision}}') end,
                on_destroy = function() print('destroy:{{revision}}') end,
                on_unload = function() print('unload:{{revision}}') end,
                systems = { {{string.Join(",", declarations)}} }
            }
            """;
    }

    private static string RejectedSource(string failure)
    {
        string source = ReloadSource(100, "new");
        return failure switch
        {
            "syntax" => "return {",
            "schema" => source.Replace("version = 1", "version = 2", StringComparison.Ordinal),
            "query" => source.Replace("'probe:position'", "'probe:absent'", StringComparison.Ordinal),
            "graph" => source.Replace("phase = 'update',", "phase = 'update', after = {'probe:missing'},", StringComparison.Ordinal),
            "hook" => source.Replace("reloaded = true", "error('candidate-hook-failed')", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] ReloadAndTrack(ReloadRunnerFixture fixture, string source,
        SimulationCodeReloadStatus expected, CancellationToken cancellationToken = default)
    {
        ModContainer owner = fixture.Owner;
        var runtime = Assert.IsType<LuaScriptRuntime>(owner.Runtime);
        object? context = runtime.Systems.Length == 0 ? null : runtime.GetContext(0);
        fixture.Write(source);
        var result = fixture.Reload(cancellationToken);
        Assert.Equal(expected, result.Status);
        if (expected != SimulationCodeReloadStatus.Success)
        {
            Assert.Same(owner, fixture.Owner);
            return [];
        }
        Assert.NotSame(owner, fixture.Owner);
        return context is null
            ? [new WeakReference(owner), new WeakReference(runtime)]
            : [new WeakReference(owner), new WeakReference(runtime), new WeakReference(context)];
    }

    private static void CollectRetired(WeakReference[] references)
    {
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.All(references, reference => Assert.False(reference.IsAlive, "Retired Lua revision is still rooted."));
    }

    private static long CommonTickBytes(ReloadRunnerFixture fixture)
    {
        for (int i = 0; i < 1000; i++) fixture.Tick();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) fixture.Tick();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class ReloadRunnerFixture : IDisposable
    {
        internal readonly ScriptLogger Logger = new();
        internal readonly LuaProbeInstaller Installer;
        internal readonly EngineRunner Runner = new(NullLoggerFactory.Instance);
        internal readonly MainThreadScheduler Scheduler = new(Environment.CurrentManagedThreadId, NullLoggerFactory.Instance);
        private readonly ModDefinition _definition;
        internal EcsDefaultWorld World => Installer.Bindings!.World;
        internal ModContainer Owner => Assert.Single(Installer.Lifecycle!.Containers);
        internal ReloadRunnerFixture(ModDefinition definition, bool useStatic, EcsUpdateSchedulerMode mode)
        {
            _definition = definition;
            Installer = new LuaProbeInstaller(definition, Logger);
            Runner.UpdateSchedulerMode = mode;
            Runner.EnableLocalCodeReload();
            if (useStatic) Runner.RegisterStaticComposition(new LuaProbeComposition(Installer, Logger));
            else { Runner.RegisterModule(Installer); Runner.RegisterModule(new ModdingLuaModuleInstaller()); }
            Runner.Setup(new Application(Side.Server), Scheduler);
            Scheduler.Execute();
        }
        internal int Entity(float value)
        {
            int entity = World.NewEntity();
            World.GetPool<LuaPosition>().Add(entity).X = value;
            return entity;
        }
        internal float Position(int entity) => World.GetPool<LuaPosition>().Read(entity).X;
        internal void Tick() => Runner.RunGameplayFrame(.02);
        internal void Write(string source) => File.WriteAllText(Path.Combine(_definition.Directory, "Shared", "main.lua"), source);
        internal SimulationCodeReloadResult Reload(CancellationToken token = default)
        {
            Task<SimulationCodeReloadResult> request = Runner.RequestCodeReloadAsync(token);
            Scheduler.RunUntilCompleted(request);
            return request.GetAwaiter().GetResult();
        }
        public void Dispose() { Runner.Destroy(); Scheduler.Dispose(); }
    }

    private sealed class ReloadResourceSampler : IDisposable
    {
        private readonly ManualResetEventSlim _stop = new();
        private readonly Thread _thread;
        private long _managedPeak, _workingSetPeak, _allocatedBytes;
        private int _threadPeak, _samples;
        private Exception? _error;
        internal long ManagedPeak => Interlocked.Read(ref _managedPeak);
        internal long WorkingSetPeak => Interlocked.Read(ref _workingSetPeak);
        internal long AllocatedBytes => Interlocked.Read(ref _allocatedBytes);
        internal int ThreadPeak => Volatile.Read(ref _threadPeak);
        internal int Samples => Volatile.Read(ref _samples);
        internal ReloadResourceSampler() => _thread = new Thread(Sample) { IsBackground = true, Name = "Lua reload resource sampler" };
        internal void Start() => _thread.Start();
        internal void Stop()
        {
            _stop.Set();
            if (_thread.IsAlive) _thread.Join();
            if (_error is not null) throw new InvalidOperationException("Reload resource sampler failed.", _error);
        }
        private void Sample()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                using var process = Process.GetCurrentProcess();
                do
                {
                    process.Refresh();
                    _managedPeak = Math.Max(_managedPeak, GC.GetTotalMemory(false));
                    _workingSetPeak = Math.Max(_workingSetPeak, process.WorkingSet64);
                    _threadPeak = Math.Max(_threadPeak, process.Threads.Count);
                    _samples++;
                }
                while (!_stop.Wait(2));
            }
            catch (Exception error) { _error = error; }
            _allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        public void Dispose() { Stop(); _stop.Dispose(); }
    }
}
