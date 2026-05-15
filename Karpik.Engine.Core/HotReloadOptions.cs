namespace Karpik.Engine.Core;

public enum HotReloadMode
{
    Disabled,
    RestartWorker
}

public sealed class HotReloadOptions
{
    public HotReloadMode Mode { get; init; }
    public string? WorkerExecutablePath { get; init; }
    public TimeSpan StateRequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan GracefulShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan WorkerConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool WaitForDebuggerOnInitialWorkerStart { get; init; }
    public bool WaitForDebuggerOnReloadWorkerStart { get; init; }

    public static HotReloadOptions Default => CreateDefault();

    public static HotReloadOptions CreateDefault()
    {
#if DEBUG
        return new HotReloadOptions
        {
            Mode = HotReloadMode.RestartWorker
        };
#else
        return new HotReloadOptions
        {
            Mode = HotReloadMode.Disabled
        };
#endif
    }
}
