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

namespace Karpik.Engine.Core.Hot;

internal static class HotReloadHandler
{
    public static event Action? OnUpdateApplication;
    
    public static void TriggerUpdateManually()
    {
        OnUpdateApplication?.Invoke();
    }
    
    // --- RELEASE: Система со встроенным .NET Hot Reload ---
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        OnUpdateApplication?.Invoke();
    }
}
