using Karpik.Engine.Client.Publish;
using Karpik.Engine.Core;

namespace ClientLauncher;

class Program
{
    static void Main(string[] args)
    {
        Client client = new();
        client.Start(new Ref<bool>());
    }
}