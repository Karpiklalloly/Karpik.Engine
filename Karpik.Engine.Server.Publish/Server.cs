using Karpik.Engine.Core;

namespace Karpik.Engine.Publish.Server;

public class Server
{
    public void Start(Ref<bool> isRunning)
    {
        CoreRunner runner = new();
        runner.Start(isRunning, Side.Server);
    }
}
