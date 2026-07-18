using Karpik.Engine.Core;

namespace Karpik.Editor;

public sealed class EditorPreviewBackendFactory : IEditorBackendFactory
{
    private readonly ProjectRuntimeResolver _runtimeResolver;

    public EditorPreviewBackendFactory(ProjectRuntimeResolver runtimeResolver)
    {
        _runtimeResolver = runtimeResolver;
    }

    public IEditorBackend Create(Side side)
    {
        ProjectRuntimeResolver.EditorRuntimeDescriptor descriptor = _runtimeResolver.Resolve(side);
        return new EditorPreviewBackend(
            new EditorPreviewController(new RuntimeLaunchOptions(side, descriptor.RunnerPath, descriptor.BundlePath, descriptor.EngineRoot)));
    }

    private sealed class EditorPreviewBackend : IEditorBackend
    {
        private readonly EditorPreviewController _controller;

        public EditorPreviewBackend(EditorPreviewController controller)
        {
            _controller = controller;
        }

        public Side Side => _controller.Side;
        public EditorPreviewState State => _controller.State;
        public int? ProcessId => _controller.ProcessId;

        public event Action<EditorPreviewState>? StateChanged
        {
            add => _controller.StateChanged += value;
            remove => _controller.StateChanged -= value;
        }

        public event Action<string>? OutputReceived
        {
            add => _controller.OutputReceived += value;
            remove => _controller.OutputReceived -= value;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            _controller.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            _controller.StopAsync(cancellationToken);

        public Task<EditorRuntimeSnapshot?> RequestSnapshotAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            _controller.RequestSnapshotAsync(timeout, cancellationToken);

        public void Dispose() => _controller.Dispose();
    }
}
