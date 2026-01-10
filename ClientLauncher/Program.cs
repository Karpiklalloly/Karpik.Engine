using Karpik.Engine.Client.Publish;
using Karpik.Engine.Core;

namespace ClientLauncher;

class Program
{
    static void Main(string[] args)
    {
        new Client().Start(new Ref<bool>(true));
    }
}