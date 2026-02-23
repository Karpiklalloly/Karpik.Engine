using Karpik.Engine.Core.Hot;

namespace DebugModule;
#if DEBUG
public class DebugThings
{
    public static void HotReload()
    {
        HotReloadHandler.TriggerUpdateManually();
    }
}
#endif