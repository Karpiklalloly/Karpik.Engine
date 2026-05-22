namespace DragonExtensions;

public sealed class ComponentLifecycleException : InvalidOperationException
{
    public string Phase { get; }
    public string WorldName { get; }
    public short WorldId { get; }
    public int EntityId { get; }
    public Type ComponentType { get; }

    public ComponentLifecycleException(
        string phase,
        EcsWorld world,
        int entityId,
        Type componentType,
        Exception innerException)
        : base(CreateMessage(phase, world, entityId, componentType), innerException)
    {
        Phase = phase;
        WorldName = world.Name;
        WorldId = world.ID;
        EntityId = entityId;
        ComponentType = componentType;
    }

    private static string CreateMessage(string phase, EcsWorld world, int entityId, Type componentType)
    {
        return $"Component lifecycle failed. Phase={phase}; World={world.Name}; WorldId={world.ID}; EntityId={entityId}; Component={componentType.FullName}";
    }
}
