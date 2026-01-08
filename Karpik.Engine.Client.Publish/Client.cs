using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Publish;

public class Client
{
    public void Start(Ref<bool> isRunning)
    {
        Bootstrap b = new();
        var mainThreadScheduler = b.Initialize(Environment.CurrentManagedThreadId, isRunning);
        Thread thread = new Thread(_ =>
        {
            DateTime lastTime = DateTime.Now;
            while (isRunning.Value)
            {
                var now = DateTime.Now;
                var deltaTime = (now - lastTime).TotalSeconds;
                lastTime = now;
                b.Loop(deltaTime);
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