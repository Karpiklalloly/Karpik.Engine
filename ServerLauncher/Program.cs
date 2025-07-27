using Karpik.Engine.Server;

namespace ServerLauncher;

class Program
{
    static void Main(string[] args)
    {
        bool isRunning = true;
        Server server = new();
        server.Init();
        server.Run(in isRunning);
    }
}