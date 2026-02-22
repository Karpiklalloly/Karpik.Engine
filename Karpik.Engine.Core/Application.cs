using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Karpik.Engine.Client.Publish")]
[assembly: InternalsVisibleTo("Karpik.Engine.Server.Publish")]
[assembly: InternalsVisibleTo("DebugModule")]
[assembly: InternalsVisibleTo("Karpik.Engine.Core.Runner")]
namespace Karpik.Engine.Core;

public class Application
{
    internal bool IsRunning { get; set; } = true;
    
    public void Stop()
    {
        IsRunning = false;
    }
}