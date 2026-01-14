using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Raylib_cs;

namespace Karpik.Engine.Client.Graphics.GRaylib;

[Module]
public class GraphicsRaylibInstaller : IModule
{
    public string Name => "Graphics.Raylib";

    public GraphicsRaylibInstaller()
    {
        NativeLibrary.SetDllImportResolver(typeof(Raylib).Assembly, (libraryName, assembly, searchPath) =>
        {
            if (libraryName == "raylib")
            {
                var processArchStr = Environment.Is64BitProcess ? "x64" : "x86";
                var pathsToTry = new[]
                {
                    // Сначала ищем в правильной папке runtimes
                    Path.Combine(AppContext.BaseDirectory, "runtimes", $"win-{processArchStr}", "native", "raylib.dll"),
                    // Потом в корне
                    Path.Combine(AppContext.BaseDirectory, "raylib.dll")
                };

                foreach (var path in pathsToTry)
                {
                    if (File.Exists(path))
                    {
                        var fileArch = GetDllArchitecture(path);
                        Console.WriteLine($"[DIAGNOSTIC] Found '{path}'. File is [{fileArch}]. Process is [{processArchStr}].");
                        
                        // Загружаем только если архитектуры совпадают
                        if (fileArch.ToString().Equals(processArchStr, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("[DIAGNOSTIC] Architectures match. Attempting to load...");
                            return NativeLibrary.Load(path);
                        }
                    }
                }
                Console.WriteLine($"[DIAGNOSTIC] Could not find a matching '{libraryName}.dll' for {processArchStr} architecture.");
            }
            return IntPtr.Zero;
        });
    }

    public void OnRegisterServices(IServiceRegister services)
    {
        var window = new RaylibWindow();
        services.Register<IWindow>(window);
        services.Register(window);

        var renderer = new RaylibRenderer();
        services.Register<IRenderer>(renderer);
        services.Register(renderer);

        var camera = new RaylibCamera();
        services.Register<ICamera>(camera);
        services.Register(camera);
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = new RaylibModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
    }

    private enum DllArchitecture { Unknown, x86, x64 }

    private static DllArchitecture GetDllArchitecture(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x5A4D) return DllArchitecture.Unknown; // "MZ"
            stream.Seek(0x3C, SeekOrigin.Begin);
            stream.Seek(reader.ReadUInt32(), SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550) return DllArchitecture.Unknown; // "PE\0\0"
            
            return reader.ReadUInt16() switch
            {
                0x014c => DllArchitecture.x86,
                0x8664 => DllArchitecture.x64,
                _ => DllArchitecture.Unknown
            };
        }
        catch { return DllArchitecture.Unknown; }
    }
}
