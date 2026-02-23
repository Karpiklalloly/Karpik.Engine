using System;
using System.IO;
using Karpik.Engine.Core;

namespace ClientLauncher;

class Program
{
    static void Main(string[] args)
    {
        var exePath = AppContext.BaseDirectory;
        Directory.SetCurrentDirectory(exePath);
        new CoreRunner().Start(new Ref<bool>(true), Side.Client);
    }
}
