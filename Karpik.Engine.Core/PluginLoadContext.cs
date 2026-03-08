using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Karpik.Engine.Core.ModuleManagement;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _shadowCopyDirectory;
    
    private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Karpik.Engine.Core.Runner",
        "Karpik.Engine.Core",
        "Dragon",
        "Karpik.Jobs",
    };

    public PluginLoadContext(string shadowCopyDirectory) : base(isCollectible: true)
    {
        _shadowCopyDirectory = shadowCopyDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (SharedAssemblyNames.Contains(assemblyName.Name ?? string.Empty))
        {
            return null;
        }
        
        string libraryName = assemblyName.Name + ".dll";
        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, libraryName),
            Path.Combine(_shadowCopyDirectory, libraryName)
        };
        
        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                return LoadFromAssemblyPath(path);
            }
        }

        return null; 
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string libraryName = unmanagedDllName;
        if (!libraryName.EndsWith(".dll") && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            libraryName += ".dll";

        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, libraryName),
            Path.Combine(AppContext.BaseDirectory, "modules", "runtimes", "win-x64", "native", libraryName),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", libraryName),
            Path.Combine(_shadowCopyDirectory, libraryName)
        };

        foreach (var path in searchPaths)
        {
            if (!File.Exists(path)) continue;
            if (NativeLibrary.TryLoad(path, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }
}
