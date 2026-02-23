using Karpik.Engine.Core;

namespace ServerLauncher;

class Program
{
    static void Main(string[] args)
    {
        var exePath = AppContext.BaseDirectory;
        Directory.SetCurrentDirectory(exePath);
        new CoreRunner().Start(new Ref<bool>(true), Side.Server);
    }
}