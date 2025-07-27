using DCFApixels.DragonECS;
using Karpik.Game;

namespace ConsoleLauncher;

class Program
{
    static void Main(string[] args)
    {
        using var launcher = new LocalGame();
        launcher.Start();
        EcsEventWorld w;
    }
}