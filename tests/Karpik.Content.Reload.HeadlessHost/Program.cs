using Autofac;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;

namespace Karpik.Content.Reload.HeadlessHost;

public sealed class HostMarker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.SetOut(new ProbeOutput(Console.Out));
        Side side = Enum.Parse<Side>(args[0]);
        bool holdChangedFrame = args.Length > 1 && args[1] == "hold";
        using var state = new ProbeState { HoldChangedFrame = holdChangedFrame };
        using var stop = new CancellationTokenSource();
        Task input = Task.Run(async () =>
        {
            try
            {
                while (await Console.In.ReadLineAsync(stop.Token) is { } command)
                {
                    if (command == "release") state.Release.Set();
                    else { Volatile.Write(ref state.StopRequested, 1); break; }
                }
            }
            catch (OperationCanceledException) { }
        });
        Task output = Task.Run(async () =>
        {
            int previous = 0;
            bool holdingReported = false;
            try
            {
                while (true)
                {
                    int observed = Volatile.Read(ref state.Observed);
                    if (observed != previous) { Console.WriteLine("observed:" + observed); previous = observed; }
                    if (!holdingReported && Volatile.Read(ref state.HoldingFrame) != 0)
                    { Console.WriteLine("holding"); holdingReported = true; }
                    await Task.Delay(2, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
        try
        {
            return await StaticEngineHost.RunAsync(side, new Composition(state), args[(holdChangedFrame ? 2 : 1)..]);
        }
        finally
        {
            stop.Cancel();
            await Task.WhenAll(input, output);
        }
    }
}

internal sealed class ProbeOutput(TextWriter output) : TextWriter
{
    public override System.Text.Encoding Encoding => output.Encoding;
    public override void WriteLine(string? value)
    {
        // Keep this fixture's stdout protocol bounded even in profiler-enabled Debug engine builds.
        if (value is not null && (value.StartsWith("observed:", StringComparison.Ordinal) || value == "holding"))
            output.WriteLine(value);
    }
}

internal sealed class ProbeState : IDisposable
{
    public int StopRequested;
    public int Observed;
    public bool HoldChangedFrame;
    public int HoldingFrame;
    public readonly ManualResetEventSlim Release = new(false);
    public void Dispose() => Release.Dispose();
}

[Module(ModuleScope.Engine)]
internal sealed class Services(ProbeState state) : IModuleInstaller
{
    public string Name => nameof(Services);
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.RegisterInstance(state);
        builder.Register(_ => new PhysicalFileSystem()).As<IFileSystem>().SingleInstance();
    }
}

[Module(ModuleScope.Simulation)]
internal sealed class Simulation : IModuleInstaller, IModule
{
    public string Name => nameof(Simulation);
    public IModule CreateModule() => this;
    public void Add(ISystemRegistry systems) => systems.Add<Observe>();
    public void OnRegisterServices(ContainerBuilder builder) => builder.Register(context => new Observe(
        context.Resolve<ProbeState>(), context.Resolve<Application>(), context.Resolve<IContentRegistry>(),
        context.Resolve<IContentChangeSource>(), context.Resolve<FileContentStore>()));
}

internal sealed class Observe(ProbeState state, Application application, IContentRegistry registry,
    IContentChangeSource changes, FileContentStore store) : ISystemAsyncInit, ISystemBegin
{
    private Core.AssetId _id;
    public async ValueTask InitAsync(CancellationToken token)
    {
        _id = store.CapturedManifest!.Entries.Single().AssetId;
        var updates = (IContentUpdateRegistry)registry;
        if (!updates.TryResolve<byte[]>(_id, out var reference)) throw new InvalidOperationException("Initial reference unavailable.");
        await registry.LoadAsync(reference, token);
        Record(reference);
    }
    public void Begin()
    {
        if (changes.Changes.Length != 0 && ((IContentUpdateRegistry)registry).TryResolve<byte[]>(_id, out var reference))
        {
            Record(reference);
            if (state.HoldChangedFrame && Volatile.Read(ref state.Observed) == 2)
            {
                // Test-only interleaving: hold simulation while the platform loop continues rendering.
                Volatile.Write(ref state.HoldingFrame, 1);
                if (!state.Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Changed simulation frame was never released.");
                if (changes.Changes.Length != 1) throw new InvalidOperationException("Platform loop reset changes during an active simulation frame.");
            }
        }
        if (Volatile.Read(ref state.StopRequested) != 0) application.Stop();
    }
    private void Record(AssetRef<byte[]> reference)
    {
        if (!registry.TryGet(reference, out var lease) || lease.Payload is not { Length: 11 } bytes)
            throw new InvalidOperationException("Prepared builtin payload unavailable.");
        Volatile.Write(ref state.Observed, bytes[9] - (byte)'0');
    }
}

internal sealed class Composition(ProbeState state) : IStaticRuntimeComposition
{
    public void RegisterModules(IStaticModuleRegistry registry)
    { registry.Add(new Services(state)); registry.Add(new Simulation()); }
    public void RegisterServices(IStaticServiceRegistry registry) { }
    public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry) { }
}
