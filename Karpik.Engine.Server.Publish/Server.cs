using Karpik.Engine.Core;

namespace Karpik.Engine.Publish.Server;

public class Server
{
    public const int TICKS_PER_SECOND = 20;
    public const int SLEEP_TIME = 1000 / TICKS_PER_SECOND;
    private readonly TimeSpan _tickInterval = TimeSpan.FromMilliseconds(SLEEP_TIME);
    private DateTime _nextTickTime;
    
    public void Start(Ref<bool> isRunning)
    {
        Bootstrap b = new();
        var mainThreadScheduler = b.Initialize(Environment.CurrentManagedThreadId, isRunning);
        Thread thread = new Thread(_ =>
        {
            _nextTickTime = DateTime.Now;
            while (isRunning.Value)
            {
                var now = DateTime.Now;
                if (now >= _nextTickTime)
                {
                    b.Loop(1.0 / TICKS_PER_SECOND);
                    _nextTickTime = now + _tickInterval;
                }
                
                Thread.Yield();
            }
            b.Shutdown();
        });
        
        thread.Start();
        while (isRunning.Value)
        {
            mainThreadScheduler.Execute();
            Thread.Yield();
        }
    }
}