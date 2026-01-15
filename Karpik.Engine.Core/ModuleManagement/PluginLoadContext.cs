using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        
        this.Resolving += OnResolving;
        this.Unloading += OnUnloading;
    }

    private void OnUnloading(AssemblyLoadContext obj)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[PluginLoadContext] Unloading context '{this.Name}' from: {_shadowCopyDirectory}");
        Console.ResetColor();
        
        this.Resolving -= OnResolving;
        this.Unloading -= OnUnloading;
    }

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        Console.WriteLine($"[PluginLoadContext] Attempting to resolve managed assembly: '{name}'");

        // 1. Основной механизм
        foreach (var resolver in _resolvers)
        {
            string? originalPath = resolver.ResolveAssemblyToPath(name);
            if (originalPath != null)
            {
                Console.WriteLine($"[PluginLoadContext] SUCCESS: Resolved '{name}' to '{originalPath}' via AssemblyDependencyResolver.");
                var shadowPath = Path.Combine(_shadowCopyDirectory, Path.GetFileName(originalPath));
                if (!File.Exists(shadowPath))
                {
                    File.Copy(originalPath, shadowPath);
                }
                return this.LoadFromAssemblyPath(shadowPath);
            }
        }
        Console.WriteLine($"[PluginLoadContext] FAILED: AssemblyDependencyResolver could not find '{name}'.");

        // 2. Резервный механизм
        var assemblyFileName = name.Name + ".dll";
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, assemblyFileName);
        Console.WriteLine($"[PluginLoadContext-Fallback] Trying fallback path: '{fallbackPath}'");
        if (File.Exists(fallbackPath))
        {
            Console.WriteLine($"[PluginLoadContext-Fallback] SUCCESS: Found file at fallback path.");
            var shadowPath = Path.Combine(_shadowCopyDirectory, Path.GetFileName(fallbackPath));
            if (!File.Exists(shadowPath))
            {
                File.Copy(fallbackPath, shadowPath);
            }
            return this.LoadFromAssemblyPath(shadowPath);
        }
        
        Console.WriteLine($"[PluginLoadContext-Fallback] FAILED: File not found at fallback path.");
        Console.WriteLine($"[PluginLoadContext] Could not resolve '{name}'. Returning null.");
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        Console.WriteLine($"[PluginLoadContext] Attempting to load native library: '{unmanagedDllName}'");
        
        // Загружаем нативные библиотеки напрямую из основной папки, чтобы избежать проблем с их зависимостями.
        foreach (var resolver in _resolvers)
        {
            string? originalPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (originalPath != null)
            {
                Console.WriteLine($"[PluginLoadContext] SUCCESS: Loading native library '{unmanagedDllName}' directly from original path: {originalPath}");
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
            Console.WriteLine($"[PluginLoadContext-Fallback] SUCCESS: Loading native library '{unmanagedDllName}' directly from AppContext.BaseDirectory.");
            return base.LoadUnmanagedDll(fallbackPath);
        }

        Console.WriteLine($"[PluginLoadContext] FAILED: Could not find native library '{unmanagedDllName}'. Passing to default loader.");
        return base.LoadUnmanagedDll(unmanagedDllName);
    }
}
