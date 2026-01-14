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
    }

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (var resolver in _resolvers)
        {
            // 1. Находим путь к зависимости в ИСХОДНОЙ папке
            string? originalPath = resolver.ResolveAssemblyToPath(name);
            if (originalPath != null)
            {
                // 2. Создаем путь для этой зависимости в НАШЕЙ теневой папке
                var shadowPath = Path.Combine(_shadowCopyDirectory, Path.GetFileName(originalPath));

                // 3. Если ее там еще нет, копируем ее
                if (!File.Exists(shadowPath))
                {
                    File.Copy(originalPath, shadowPath);
                }

                // 4. Загружаем из теневой папки
                return this.LoadFromAssemblyPath(shadowPath);
            }
        }
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var resolver in _resolvers)
        {
            string? originalPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (originalPath != null)
            {
                var shadowPath = Path.Combine(_shadowCopyDirectory, Path.GetFileName(originalPath));
                if (!File.Exists(shadowPath))
                {
                    File.Copy(originalPath, shadowPath);
                }
                return base.LoadUnmanagedDll(shadowPath);
            }
        }
        
        var exeDirLibraryPath = Path.Combine(AppContext.BaseDirectory, unmanagedDllName + ".dll");
        if (File.Exists(exeDirLibraryPath))
        {
            return base.LoadUnmanagedDll(exeDirLibraryPath);
        }

        return base.LoadUnmanagedDll(unmanagedDllName);
    }
}
