using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Publish;

public class Client
{
    public void Start(Ref<bool> isRunning)
    {
        CoreRunner runner = new();
        runner.Start(isRunning, Side.Client);
    }
}
