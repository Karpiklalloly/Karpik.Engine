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
    private readonly List<(IntPtr, string)> _nativeHandles = new();

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
            Console.WriteLine($"Unload {handle.Item2}");
            NativeLibrary.Free(handle.Item1);
        }
        _nativeHandles.Clear();
        
        Resolving -= OnResolving;
        Unloading -= OnUnloading;
    }

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        // --- ИЗМЕНЕНИЕ: Самый высокий приоритет — проверка уже скопированных файлов ---
        // Так как ModuleLoader уже скопировал все DLL, мы сначала смотрим в shadow folder.
        // Это позволяет загружать зависимости, даже если оригиналы были удалены.
        
        var expectedFileName = name.Name + ".dll";
        var shadowPathToCheck = Path.Combine(_shadowCopyDirectory, expectedFileName);
        Console.WriteLine($"Загружаем {name.Name}");
        
        if (File.Exists(shadowPathToCheck))
        {
            try 
            {
                return LoadFromAssemblyPath(shadowPathToCheck);
            }
            catch (Exception)
            {
                // Если файл поврежден или это не сборка, идем дальше к резолверам
            }
        }
        // -----------------------------------------------------------------------------

        // 1. Поиск через стандартные резолверы (.deps.json)
        foreach (var resolver in _resolvers)
        {
            string? originalPath = resolver.ResolveAssemblyToPath(name);
            if (originalPath != null)
            {
                // Если резолвер нашел путь, но мы не нашли его в теневой папке выше (например, имя файла отличается),
                // то копируем сейчас.
                var shadowPath = Path.Combine(_shadowCopyDirectory, Path.GetFileName(originalPath));
                
                // Проверяем существование ОРИГИНАЛА перед копированием
                if (File.Exists(originalPath)) 
                {
                    if (!File.Exists(shadowPath))
                    {
                        File.Copy(originalPath, shadowPath);
                    }
                    return LoadFromAssemblyPath(shadowPath);
                }
            }
        }
        
        // 2. Резервный механизм (Fallback) - поиск в папке запуска
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, expectedFileName);
        if (File.Exists(fallbackPath))
        {
            var shadowPath = Path.Combine(_shadowCopyDirectory, Path.GetFileName(fallbackPath));
            if (!File.Exists(shadowPath))
            {
                File.Copy(fallbackPath, shadowPath);
            }
            return LoadFromAssemblyPath(shadowPath);
        }
        
        // Если мы здесь, значит файла нет ни в теневой, ни в оригинальной папке
        Console.WriteLine($"[PluginLoadContext] Could not resolve '{name}'.");
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        // Для нативных библиотек логика похожая:
        // 1. Проверяем теневую папку (если вы копируете и .dll/.so/.dylib нативных либ)
        var shadowNativePath = Path.Combine(_shadowCopyDirectory, unmanagedDllName);
        if (!shadowNativePath.EndsWith(".dll") && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) 
            shadowNativePath += ".dll";
            
        if (File.Exists(shadowNativePath))
        {
            if (NativeLibrary.TryLoad(shadowNativePath, out var handle))
            {
                _nativeHandles.Add((handle, shadowNativePath));
                return handle;
            }
        }

        string? path = null;
        
        // 2. Поиск через resolver
        foreach (var resolver in _resolvers)
        {
            path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path != null) break;
        }

        // 3. Поиск в базовой директории
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

        if (path != null && File.Exists(path))
        {
            if (NativeLibrary.TryLoad(path, out var handle))
            {
                _nativeHandles.Add((handle, path));
                return handle;
            }
        }

        return base.LoadUnmanagedDll(unmanagedDllName);
    }
}
