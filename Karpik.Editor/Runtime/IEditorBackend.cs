using Karpik.Engine.Core;

namespace Karpik.Editor;

public interface IEditorBackend : IDisposable
{
    Side Side { get; }
    EditorPreviewState State { get; }
    int? ProcessId { get; }

    event Action<EditorPreviewState>? StateChanged;
    event Action<string>? OutputReceived;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<EditorRuntimeSnapshot?> RequestSnapshotAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public interface IEditorBackendFactory
{
    IEditorBackend Create(Side side);
}
