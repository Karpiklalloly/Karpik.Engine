using System;
using System.IO;
using Karpik.Engine.Client.Publish;
using Karpik.Engine.Core;

namespace ClientLauncher;

class Program
{
    static void Main(string[] args)
    {
        // --- ИСПРАВЛЕНИЕ ---
        // Гарантируем, что рабочая директория всегда равна папке с exe-файлом.
        // Это критически важно для нативных библиотек (таких как Raylib), которые
        // ищут свои зависимости относительно текущей рабочей директории, а не AppContext.BaseDirectory.
        var exePath = AppContext.BaseDirectory;
        Directory.SetCurrentDirectory(exePath);
        Console.WriteLine($"[Launcher] Working directory set to: {exePath}");
        // --- КОНЕЦ ИСПРАВЛЕНИЯ ---

        new Client().Start(new Ref<bool>(true));
    }
}
