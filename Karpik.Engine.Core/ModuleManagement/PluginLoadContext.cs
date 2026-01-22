using System.Reflection;
using System.Runtime.Loader;

namespace Karpik.Engine.Core.ModuleManagement;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _shadowCopyDirectory;
    private readonly List<AssemblyDependencyResolver> _resolvers;

    public PluginLoadContext(IEnumerable<string> pluginPaths, string shadowCopyDirectory) : base(isCollectible: true)
    {
        _shadowCopyDirectory = shadowCopyDirectory;
        _resolvers = pluginPaths.Select(p => new AssemblyDependencyResolver(p)).ToList();
        
        Resolving += OnResolving;
        Unloading += OnUnloading;
    }

    private void OnUnloading(AssemblyLoadContext obj)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.ResetColor();
        
        Resolving -= OnResolving;
        Unloading -= OnUnloading;
    }

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        // 1. Основной механизм
        foreach (var resolver in _resolvers)
        {
            string? originalPath = resolver.ResolveAssemblyToPath(name);
            if (originalPath != null)
            {
                var shadowPath = Path.Combine(_shadowCopyDirectory, Path.GetFileName(originalPath));
                if (!File.Exists(shadowPath))
                {
                    File.Copy(originalPath, shadowPath);
                }
                return this.LoadFromAssemblyPath(shadowPath);
            }
        }
        
        // 2. Резервный механизм
        var assemblyFileName = name.Name + ".dll";
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, assemblyFileName);
        if (File.Exists(fallbackPath))
        {
            var shadowPath = Path.Combine(_shadowCopyDirectory, Path.GetFileName(fallbackPath));
            if (!File.Exists(shadowPath))
            {
                File.Copy(fallbackPath, shadowPath);
            }
            return LoadFromAssemblyPath(shadowPath);
        }
        
        Console.WriteLine($"[PluginLoadContext-Fallback] FAILED: File not found at fallback path.");
        Console.WriteLine($"[PluginLoadContext] Could not resolve '{name}'. Returning null.");
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        // Загружаем нативные библиотеки напрямую из основной папки, чтобы избежать проблем с их зависимостями.
        foreach (var resolver in _resolvers)
        {
            string? originalPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (originalPath != null)
            {
                return base.LoadUnmanagedDll(originalPath);
            }
        }
        
        var unmanagedFileName = unmanagedDllName;
        if (!unmanagedFileName.EndsWith(".dll"))
        {
            unmanagedFileName += ".dll";
        }
        
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, unmanagedFileName);
        if (File.Exists(fallbackPath))
        {
            return base.LoadUnmanagedDll(fallbackPath);
        }

        Console.WriteLine($"[PluginLoadContext] FAILED: Could not find native library '{unmanagedDllName}'. Passing to default loader.");
        return base.LoadUnmanagedDll(unmanagedDllName);
    }
}
