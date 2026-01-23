using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Karpik.Engine.Core.ModuleManagement;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _shadowCopyDirectory;
    private readonly List<AssemblyDependencyResolver> _resolvers;
    private readonly List<IntPtr> _nativeHandles = new();

    public PluginLoadContext(IEnumerable<string> pluginPaths, string shadowCopyDirectory) : base(isCollectible: true)
    {
        _shadowCopyDirectory = shadowCopyDirectory;
        _resolvers = pluginPaths.Select(p => new AssemblyDependencyResolver(p)).ToList();
        
        Resolving += OnResolving;
        Unloading += OnUnloading;
    }

    private void OnUnloading(AssemblyLoadContext obj)
    {
        foreach (var handle in _nativeHandles)
        {
            NativeLibrary.Free(handle);
        }
        _nativeHandles.Clear();
        
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
        string? path = null;
        
        // 1. Поиск через resolver
        foreach (var resolver in _resolvers)
        {
            path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path != null) break;
        }

        // 2. Поиск в базовой директории
        if (path == null)
        {
            var unmanagedFileName = unmanagedDllName;
            if (!unmanagedFileName.EndsWith(".dll"))
            {
                unmanagedFileName += ".dll";
            }
            var fallbackPath = Path.Combine(AppContext.BaseDirectory, unmanagedFileName);
            if (File.Exists(fallbackPath))
            {
                path = fallbackPath;
            }
        }

        if (path != null)
        {
            // Используем NativeLibrary.Load, чтобы получить хендл и контролировать выгрузку
            if (NativeLibrary.TryLoad(path, out var handle))
            {
                _nativeHandles.Add(handle);
                return handle;
            }
        }

        Console.WriteLine($"[PluginLoadContext] FAILED: Could not find native library '{unmanagedDllName}'. Passing to default loader.");
        return base.LoadUnmanagedDll(unmanagedDllName);
    }
}
