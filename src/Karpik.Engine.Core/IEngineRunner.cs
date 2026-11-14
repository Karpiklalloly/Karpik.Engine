namespace Karpik.Engine.Core;

internal interface IEngineRunner
{
    public Task SetupAsync(Application application, MainThreadScheduler scheduler, ClientFrameMetrics clientFrameMetrics,
        Dictionary<string, byte[]>? hotReloadData = null, CancellationToken cancellationToken = default);

    public void Setup(Application application, MainThreadScheduler scheduler, ClientFrameMetrics clientFrameMetrics,
        Dictionary<string, byte[]>? hotReloadData = null);

    public void RegisterTypes(Type[] types);

    public void Run(double dt);

    public void RunMainThreadBegin();

    public void RunMainThreadFrameBegin();

    public void RunGameplayFrame(double dt);

    public void RunRender();

    public bool IsApplicationRunning { get; }

    bool IsCodeReloading => false;
    double CodeReloadPausedSeconds => 0;
    Task<SimulationCodeReloadResult> RequestCodeReloadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new SimulationCodeReloadResult(SimulationCodeReloadStatus.Unsupported));
    Task DrainCodeReloadForShutdownAsync() => Task.CompletedTask;

    public void Destroy();

    public Task DestroyAsync();

    public void LogJobError(Exception exception, Microsoft.Extensions.Logging.ILogger fallback) =>
        Microsoft.Extensions.Logging.LoggerExtensions.LogError(fallback, exception, "Job failed");

    public Dictionary<string, byte[]> GetHotReloadData();

    public EditorRuntimeSnapshot CaptureEditorSnapshot();
}
