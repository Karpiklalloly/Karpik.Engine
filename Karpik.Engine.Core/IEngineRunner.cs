namespace Karpik.Engine.Core;

internal interface IEngineRunner
{
    public Task SetupAsync(Application application, MainThreadScheduler scheduler, ClientFrameMetrics clientFrameMetrics,
        Dictionary<string, byte[]>? hotReloadData = null);

    public void Setup(Application application, MainThreadScheduler scheduler, ClientFrameMetrics clientFrameMetrics,
        Dictionary<string, byte[]>? hotReloadData = null);

    public void RegisterTypes(Type[] types);

    public void Run(double dt);

    public void RunMainThreadBegin();

    public void RunMainThreadFrameBegin();

    public void RunGameplayFrame(double dt);

    public void RunRender();

    public bool IsApplicationRunning { get; }

    public void Destroy();

    public Task DestroyAsync();

    public void LogJobError(Exception exception, Microsoft.Extensions.Logging.ILogger fallback) =>
        Microsoft.Extensions.Logging.LoggerExtensions.LogError(fallback, exception, "Job failed");

    public Dictionary<string, byte[]> GetHotReloadData();

    public EditorRuntimeSnapshot CaptureEditorSnapshot();
}
