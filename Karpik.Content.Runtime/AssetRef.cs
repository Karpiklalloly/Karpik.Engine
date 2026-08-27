using DCFApixels.DragonECS;
using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

public readonly struct AssetRef<T> : IEquatable<AssetRef<T>>, IComparable<AssetRef<T>>, IEcsComponent
{
    public readonly AssetId Id;
    public readonly uint Version;
    public readonly string LogicalName;

    public AssetRef(AssetId id, string logicalName, uint version = 1)
    {
        Id = id;
        LogicalName = logicalName;
        Version = version;
    }

    public AssetRef(string guid, string logicalName, uint version = 1)
        : this(AssetId.Parse(guid), logicalName, version) { }

    public bool IsAlive(ContentRegistry registry) => registry != null && registry.IsAlive(this);

    public bool Equals(AssetRef<T> other) => Id.Equals(other.Id) && Version == other.Version;

    public int CompareTo(AssetRef<T> other) => Id.CompareTo(other.Id);

    public override bool Equals(object? obj) => obj is AssetRef<T> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Version);

    public string ToCanonicalString() => Id.ToCanonicalString();

    public static bool operator ==(AssetRef<T> left, AssetRef<T> right) => left.Equals(right);
    public static bool operator !=(AssetRef<T> left, AssetRef<T> right) => !left.Equals(right);
}

public readonly struct AssetRef : IComparable<AssetRef>
{
    public readonly AssetId Id;

    public AssetRef(AssetId id)
    {
        Id = id;
    }

    public int CompareTo(AssetRef other) => Id.CompareTo(other.Id);
}

// Minimal stub for Task 2 contracts — full registry with single-flight, versioned slots,
// and TryGet/LoadAsync is implemented in Task 4. This stub provides IsAlive needed for
// AssetRef<T>.IsAlive and AssetLease<T>.IsAlive to compile and for AssetRefTests to pass.
public sealed class ContentRegistry
{
    public bool IsAlive<T>(AssetRef<T> r) => false;
    public bool IsAlive(AssetId id, uint version) => false;
}
