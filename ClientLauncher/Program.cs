using Karpik.Engine.Client;

namespace ClientLauncher;

class Program
{
    static void Main(string[] args)
    {
        bool isRunning = true;
        Client client = new();
        client.Init();
        client.Run(in isRunning);
    }
}