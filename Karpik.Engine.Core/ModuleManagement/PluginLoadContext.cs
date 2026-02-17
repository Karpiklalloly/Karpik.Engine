using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Karpik.Engine.Core.ModuleManagement;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _shadowCopyDirectory;

    public PluginLoadContext(string shadowCopyDirectory) : base(isCollectible: true)
    {
        _shadowCopyDirectory = shadowCopyDirectory;
        Unloading += OnUnloading;
    }

    private void OnUnloading(AssemblyLoadContext obj)
    {
        Console.WriteLine($"Unloading {obj.Name}");
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 1. Ищем сборку в теневой папке
        string assemblyPath = Path.Combine(_shadowCopyDirectory, assemblyName.Name + ".dll");
        if (File.Exists(assemblyPath))
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // 2. Если не нашли, позволяем системе найти её в Default контексте (например, системные либы)
        return null; 
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        // 1. Пытаемся использовать стандартный резолвер (он знает про папки runtimes/win-x64/...)
        // Если ты используешь AssemblyDependencyResolver, верни его. 
        // Если нет - используем ручной поиск:
    
        string libraryName = unmanagedDllName;
        if (!libraryName.EndsWith(".dll") && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            libraryName += ".dll";

        // Список мест для поиска в порядке приоритета:
        var searchPaths = new[]
        {
            // А. Папка исполнения (корневая)
            Path.Combine(AppContext.BaseDirectory, libraryName),
            
            // Б. Стандартный путь NuGet для x64 Windows у плагинов
            Path.Combine(AppContext.BaseDirectory, "modules", "runtimes", "win-x64", "native", libraryName),
        
            // В. Стандартный путь NuGet для x64 Windows
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", libraryName),
        
            // Г. Теневая папка (на случай, если нативка попала туда)
            Path.Combine(_shadowCopyDirectory, libraryName)
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                if (NativeLibrary.TryLoad(path, out var handle))
                {
                    // Console.WriteLine($"[PluginLoadContext] Native library loaded: {path}");
                    return handle;
                }
            }
        }

        return IntPtr.Zero;
    }
}
