using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Karpik.Engine.Client.Publish")]
[assembly: InternalsVisibleTo("Karpik.Engine.Server.Publish")]
[assembly: InternalsVisibleTo("DebugModule")]
[assembly: InternalsVisibleTo("Karpik.Engine.Core.Runner")]
namespace Karpik.Engine.Core;

public class Application
{
    public const int TICKS_PER_SECOND = 50;
    public const int SLEEP_TIME = 1000 / TICKS_PER_SECOND;
    public const double TICK_DT = 1.0 / TICKS_PER_SECOND;
    
    public Side ApplicationSide { get; }
    internal bool IsRunning { get; set; } = true;

    public Application(Side side)
    {
        ApplicationSide = side;
    }
    
    public void Stop()
    {
        IsRunning = false;
    }
}
