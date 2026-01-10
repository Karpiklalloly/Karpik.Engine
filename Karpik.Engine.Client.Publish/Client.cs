using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Publish;

public class Client
{
    public void Start(Ref<bool> isRunning)
    {
        Bootstrap b = new();
        var mainThreadScheduler = b.Initialize(Environment.CurrentManagedThreadId, isRunning);
        DateTime lastTime = DateTime.Now;
        while (isRunning.Value)
        {
            var now = DateTime.Now;
            var deltaTime = (now - lastTime).TotalSeconds;
            lastTime = now;
            b.Loop(deltaTime);
            mainThreadScheduler.Execute();
            Thread.Yield();
        }
    }
}