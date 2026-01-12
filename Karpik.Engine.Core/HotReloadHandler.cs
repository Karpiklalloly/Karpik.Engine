using System;
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Karpik.Engine.Core.HotReloadHandler))]

namespace Karpik.Engine.Core;

/// <summary>
/// Handles .NET Hot Reload updates. This class is discovered by the runtime via the assembly attribute.
/// Its existence is ensured by a hard reference in the application's entry point (Program.Main).
/// </summary>
internal static class HotReloadHandler
{
    /// <summary>
    /// This event is fired when the application code is updated.
    /// The Bootstrap instance subscribes to this to trigger a pipeline rebuild.
    /// </summary>
    public static event Action<Type[]?>? OnUpdateApplication;
    
    // This method is called by the runtime when a rude edit is applied.
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(">>> [HOT RELOAD SUCCESS] UpdateApplication triggered in Core! <<<");
        Console.ResetColor();

        // Fire the event to notify listeners (like Bootstrap)
        OnUpdateApplication?.Invoke(updatedTypes);
    }
}
