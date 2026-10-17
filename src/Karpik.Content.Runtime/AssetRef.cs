using DCFApixels.DragonECS;
using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

public readonly struct AssetRef<T> : IEquatable<AssetRef<T>>, IComparable<AssetRef<T>>, IEcsComponent
{
    public readonly AssetId Id;
    public readonly uint Version;

    public AssetRef(AssetId id, uint version = 1)
    {
        Id = id;
        Version = version;
    }

    public AssetRef(string guid, uint version = 1)
        : this(AssetId.Parse(guid), version) { }

    public bool IsAlive(ContentRegistry? registry) => registry != null && registry.IsAlive(this);

    public bool Equals(AssetRef<T> other) => Id.Equals(other.Id) && Version == other.Version;

    public int CompareTo(AssetRef<T> other)
    {
        int cmp = Id.CompareTo(other.Id);
        if (cmp != 0) return cmp;
        return Version.CompareTo(other.Version);
    }

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
