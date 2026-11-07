using Autofac;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Content.Transport;
using Karpik.Engine.Core.FileSystem;
using RuntimeOutcome = Karpik.Content.Runtime.ContentReloadOutcome;
using WireOutcome = Karpik.Content.Transport.ContentReloadOutcome;

namespace Karpik.Engine.Core.Runner;

internal sealed class EmptyContentChangeSource : IContentChangeSource, IContentReloadLifetime
{
    public ReadOnlySpan<ContentAssetChange> Changes => ReadOnlySpan<ContentAssetChange>.Empty;
    public ulong Sequence => 0;
    public void RequestStop() { }
}

/// <summary>Cold endpoint lifecycle. Coordinator retirement is disposed separately, after renderer owners.</summary>
internal sealed class ContentReloadHostRuntime : IStartable, IAsyncDisposable
{
    private readonly ContentReloadAddress _address;
    private readonly ContentReloadCoordinator _coordinator;
    private readonly object _stopGate = new();
    private ContentReloadEndpoint? _endpoint;
    private Task? _stop;
    public ContentReloadHostRuntime(string instanceId, Application application, IFileSystem fileSystem,
        FileContentStore store, ContentRegistry registry, ContentReloadCoordinator coordinator)
    {
        _ = registry; // The same initialized registry must precede endpoint activation and outlive it.
        _coordinator = coordinator;
        if (!store.IsGenerationCaptured || !store.IdentityKnown || string.IsNullOrWhiteSpace(store.Namespace))
            throw new InvalidOperationException("Content reload requires writable generation ownership and a validated namespace/target. Rebuild Content into writable output, then restart this development runtime.");
        AssetTarget target = application.ApplicationSide switch
        {
            Side.Client => AssetTarget.Client,
            Side.Server => AssetTarget.Server,
            _ => throw new InvalidOperationException("Content reload runtime side must be Client or Server.")
        };
        if (store.Target != target)
            throw new InvalidOperationException("Content reload generation target does not match the runtime side.");
        _address = ContentReloadAddress.Create(fileSystem.ContentPath, instanceId, store.Namespace,
            target == AssetTarget.Client ? ContentReloadTarget.Client : ContentReloadTarget.Server);
    }
    public void Start() => _endpoint = new ContentReloadEndpoint(_address, ApplyAsync);
    private async Task<ContentReloadResponse> ApplyAsync(ContentReloadRequest request, CancellationToken stopping)
    {
        try
        {
            stopping.ThrowIfCancellationRequested();
            using var generation = GenerationContentStore.Open(_address.OutputRoot, request.GenerationId);
            AssetTarget expected = _address.Target == ContentReloadTarget.Client ? AssetTarget.Client : AssetTarget.Server;
            if (!generation.IdentityKnown || generation.Namespace != _address.Namespace || generation.Target != expected)
                return new ContentReloadResponse(1, request.RequestId, WireOutcome.Failed, "Published generation namespace/target does not match this runtime.");
            var result = await _coordinator.EnqueueAsync(generation.Manifest, generation).ConfigureAwait(false);
            WireOutcome outcome = result.Outcome switch
            {
                RuntimeOutcome.Applied => WireOutcome.Applied,
                RuntimeOutcome.Superseded => WireOutcome.Superseded,
                _ => WireOutcome.Failed
            };
            return new ContentReloadResponse(1, request.RequestId, outcome, BoundError(result.Error));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException or ArgumentException)
        {
            return new ContentReloadResponse(1, request.RequestId, WireOutcome.Failed, BoundError(error.Message));
        }
    }
    private static string? BoundError(string? error) => error is { Length: > 1024 } ? error[..1024] : error;
    public void RequestStop()
    {
        _coordinator.RequestStop();
        lock (_stopGate) _stop ??= _endpoint?.DisposeAsync().AsTask() ?? Task.CompletedTask;
    }
    public async ValueTask DisposeAsync()
    {
        RequestStop();
        await _stop!.ConfigureAwait(false);
        if (_endpoint != null) await _endpoint.Completion.ConfigureAwait(false);
    }
}

internal sealed class ContentReloadBoundarySystem(ContentReloadCoordinator coordinator, ContentReloadHostRuntime host)
    : ISystemMainThreadFrameBegin, ISystemAsyncDestroy
{
    public void MainThreadFrameBegin() => coordinator.PublishBoundary();
    public ValueTask DestroyAsync() { host.RequestStop(); return ValueTask.CompletedTask; }
}
