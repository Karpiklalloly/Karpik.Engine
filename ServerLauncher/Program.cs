using Karpik.Engine.Core;
using Karpik.Engine.Publish.Server;

namespace ServerLauncher;

class Program
{
    static void Main(string[] args)
    {
        new Server().Start(new Ref<bool>(true));
    }
}