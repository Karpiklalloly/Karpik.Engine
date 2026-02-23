namespace Karpik.Engine.Shared.Modding;


public static class EventModMethods
{
    public static void on_load() { }
    public const string OnLoad = nameof(on_load);
    
    public static void on_unload() { }
    public const string OnUnload = nameof(on_unload);
    
    public static void on_start() { }
    public const string OnStart = nameof(on_start);
    
    public static void on_update(double dt) { }
    public const string OnUpdate = nameof(on_update);
}