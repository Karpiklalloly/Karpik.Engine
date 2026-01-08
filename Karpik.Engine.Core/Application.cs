namespace Karpik.Engine.Core;

public class Application
{
    internal bool IsRunning { get; set; } = true;
    
    public void Stop()
    {
        IsRunning = false;
    }
}