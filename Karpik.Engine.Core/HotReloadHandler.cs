using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Runtime.CompilerServices;

#if !DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Karpik.Engine.Core.Hot.HotReloadHandler))]
#endif

// РЕШЕНИЕ: Делаем внутренние классы Core видимыми для Client.Publish
[assembly: InternalsVisibleTo("Karpik.Engine.Client.Publish")]
[assembly: InternalsVisibleTo("DebugModule")]

namespace Karpik.Engine.Core.Hot;

internal static class HotReloadHandler
{
    public static event Action? OnUpdateApplication;

#if DEBUG
    // --- DEBUG: Система с FileSystemWatcher для полной перезагрузки ---
    private static FileSystemWatcher? _watcher;
    private static Timer? _debounceTimer;
    private static readonly object _lock = new object();
    private static readonly HashSet<string> _watchedAssemblies = new();

    public static void Initialize(IEnumerable<string> assemblyNamesToWatch)
    {
        _watchedAssemblies.Clear();
        foreach (var name in assemblyNamesToWatch)
        {
            _watchedAssemblies.Add(name);
        }

        var path = AppContext.BaseDirectory;
        _watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            Filter = "*.dll",
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };

        _watcher.Changed += OnDllChanged;
        Console.WriteLine($"[HotReloadHandler-DEBUG] Watching for DLL changes in: {path}");
        Console.WriteLine($"[HotReloadHandler-DEBUG] Watching assemblies: {string.Join(", ", _watchedAssemblies)}");
    }

    private static void OnDllChanged(object sender, FileSystemEventArgs e)
    {
        var changedAssemblyName = Path.GetFileNameWithoutExtension(e.Name);
        if (e.Name == null || !_watchedAssemblies.Contains(changedAssemblyName))
        {
            return;
        }

        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                Console.WriteLine($"[HotReloadHandler-DEBUG] Detected change in watched assembly {e.Name}. Triggering full reload...");
                OnUpdateApplication?.Invoke();
            }, null, 500, Timeout.Infinite);
        }
    }

    public static void Shutdown()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }

    /// <summary>
    /// Позволяет запустить перезагрузку вручную из дебаг-модуля.
    /// </summary>
    public static void TriggerUpdateManually()
    {
        Console.WriteLine("[HotReloadHandler-DEBUG] Manual update triggered.");
        OnUpdateApplication?.Invoke();
    }
#else
    // --- RELEASE: Система со встроенным .NET Hot Reload ---
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        Console.WriteLine("[HotReloadHandler-RELEASE] Metadata update detected. Triggering light reload...");
        OnUpdateApplication?.Invoke();
    }
#endif
}
