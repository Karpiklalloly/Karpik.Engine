using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.IO;

namespace Karpik.Engine.Core.ModuleManagement;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly List<AssemblyDependencyResolver> _resolvers;

    public PluginLoadContext(IEnumerable<string> pluginPaths) : base(isCollectible: true)
    {
        _resolvers = pluginPaths.Select(p => new AssemblyDependencyResolver(p)).ToList();
        this.Resolving += OnResolving;
    }

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (var resolver in _resolvers)
        {
            string? assemblyPath = resolver.ResolveAssemblyToPath(name);
            if (assemblyPath != null)
            {
                return this.LoadFromAssemblyPath(assemblyPath);
            }
        }
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        // 1. Поиск через .deps.json (самый надежный способ)
        foreach (var resolver in _resolvers)
        {
            string? libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                // Используем встроенный механизм базового класса, но с полным путем.
                return base.LoadUnmanagedDll(libraryPath);
            }
        }
        
        // 2. Фоллбэк на поиск рядом с exe
        var exeDirLibraryPath = Path.Combine(AppContext.BaseDirectory, unmanagedDllName + ".dll");
        if (File.Exists(exeDirLibraryPath))
        {
            return base.LoadUnmanagedDll(exeDirLibraryPath);
        }

        // 3. Стандартное поведение
        return base.LoadUnmanagedDll(unmanagedDllName);
    }
}
