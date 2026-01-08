using Karpik.Engine.Core;
using Karpik.Engine.Publish.Server;

namespace ServerLauncher;

class Program
{
    static void Main(string[] args)
    {
        Server client = new();
        client.Start(new Ref<bool>());
    }
}