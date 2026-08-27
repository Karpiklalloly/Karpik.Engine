namespace Karpik.Content.Core;

public readonly struct AssetId : IEquatable<AssetId>, IComparable<AssetId>, IComparable
{
    private readonly Guid _value;

    public AssetId(Guid value)
    {
        _value = value;
    }

    public Guid Value => _value;

    public static AssetId New() => new(Guid.NewGuid());

    public static bool TryParse(string? text, out AssetId result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!Guid.TryParse(text, out Guid guid))
        {
            return false;
        }

        result = new AssetId(guid);
        return true;
    }

    public static AssetId Parse(string text)
    {
        if (!TryParse(text, out AssetId result))
        {
            throw new FormatException($"Invalid AssetId '{text}'. Expected canonical GUID in D format.");
        }

        return result;
    }

    public override string ToString() => _value.ToString("D").ToLowerInvariant();

    public string ToCanonicalString() => ToString();

    public bool Equals(AssetId other) => _value.Equals(other._value);

    public override bool Equals(object? obj) => obj is AssetId other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();

    public int CompareTo(AssetId other) => _value.CompareTo(other._value);

    public int CompareTo(object? obj)
    {
        if (obj is AssetId other) return CompareTo(other);
        throw new ArgumentException($"Object is not an {nameof(AssetId)}.");
    }

    public static bool operator ==(AssetId left, AssetId right) => left.Equals(right);
    public static bool operator !=(AssetId left, AssetId right) => !left.Equals(right);
    public static bool operator <(AssetId left, AssetId right) => left.CompareTo(right) < 0;
    public static bool operator >(AssetId left, AssetId right) => left.CompareTo(right) > 0;
    public static bool operator <=(AssetId left, AssetId right) => left.CompareTo(right) <= 0;
    public static bool operator >=(AssetId left, AssetId right) => left.CompareTo(right) >= 0;
}
