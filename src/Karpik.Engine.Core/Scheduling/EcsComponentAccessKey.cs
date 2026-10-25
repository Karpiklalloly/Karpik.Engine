namespace Karpik.Engine.Shared.ECS.Scheduling;

public readonly struct EcsComponentAccessKey : IEquatable<EcsComponentAccessKey>
{
    private EcsComponentAccessKey(Type? clrType, string? dynamicId) { ClrType = clrType; DynamicId = dynamicId; }
    public Type? ClrType { get; }
    public string? DynamicId { get; }
    public static EcsComponentAccessKey ForClr(Type type)
    {
        ArgumentNullException.ThrowIfNull(type); return new(type, null);
    }
    public static EcsComponentAccessKey ForDynamic(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id); return new(null, id);
    }
    public bool Equals(EcsComponentAccessKey other) => ClrType == other.ClrType && DynamicId == other.DynamicId;
    public override bool Equals(object? obj) => obj is EcsComponentAccessKey key && Equals(key);
    public override int GetHashCode() => ClrType?.GetHashCode() ?? (DynamicId is null ? 0 : StringComparer.Ordinal.GetHashCode(DynamicId));
    public static bool operator ==(EcsComponentAccessKey left, EcsComponentAccessKey right) => left.Equals(right);
    public static bool operator !=(EcsComponentAccessKey left, EcsComponentAccessKey right) => !left.Equals(right);
}
