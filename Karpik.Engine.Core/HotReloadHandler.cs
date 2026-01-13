using System;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

[assembly: MetadataUpdateHandler(typeof(Karpik.Engine.Core.Hot.HotReloadHandler))]
[assembly: InternalsVisibleTo("DebugModule")]
namespace Karpik.Engine.Core.Hot;

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
    public static event Action? OnUpdateApplication;
    
    // This method is called by the runtime when a rude edit is applied.
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine();
        Console.WriteLine(">>> [HOT RELOAD SUCCESS] UpdateApplication triggered in Core! <<<");
        Console.WriteLine();
        Console.ResetColor();

        TriggerUpdate();
    }

    public static void TriggerUpdateManually()
    {
        if (!IsDebuggerFromRider())
        {
            TriggerUpdate();
        }
    }

    private static void TriggerUpdate()
    {
        OnUpdateApplication?.Invoke();
    }
    
    private static bool IsDebuggerFromRider()
    {
        return false;
    }
}
