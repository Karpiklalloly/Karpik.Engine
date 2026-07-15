using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Karpik.Engine.Core.ModuleManagement;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _shadowCopyDirectory;
    private readonly string? _bundleRoot;
    private readonly bool _allowAppContextFallback;
    
    private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Karpik.Engine.Core.Runner",
        "Karpik.Engine.Core",
        "Dragon",
        "Karpik.Jobs",
    };

    public PluginLoadContext(
        string shadowCopyDirectory,
        string? bundleRoot = null,
        bool allowAppContextFallback = true) : base(isCollectible: bundleRoot is not null)
    {
        _shadowCopyDirectory = shadowCopyDirectory;
        _bundleRoot = bundleRoot;
        _allowAppContextFallback = allowAppContextFallback;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (SharedAssemblyNames.Contains(assemblyName.Name ?? string.Empty))
        {
            return null;
        }
        
        string libraryName = assemblyName.Name + ".dll";
        var searchPaths = CandidatePaths(libraryName, native: false);
        
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

        var searchPaths = CandidatePaths(libraryName, native: true);

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

    private IEnumerable<string> CandidatePaths(string libraryName, bool native)
    {
        yield return Path.Combine(_shadowCopyDirectory, libraryName);
        if (native)
        {
            yield return Path.Combine(_shadowCopyDirectory, "runtimes", "win-x64", "native", libraryName);
        }
        if (_bundleRoot is not null)
        {
            yield return Path.Combine(_bundleRoot, libraryName);
            if (native)
            {
                yield return Path.Combine(_bundleRoot, "runtimes", "win-x64", "native", libraryName);
            }
        }
        if (!_allowAppContextFallback)
        {
            yield break;
        }
        yield return Path.Combine(AppContext.BaseDirectory, libraryName);
        if (native)
        {
            yield return Path.Combine(AppContext.BaseDirectory, "modules", "runtimes", "win-x64", "native", libraryName);
            yield return Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", libraryName);
        }
    }
}
