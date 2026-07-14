using System.Text.Json;

namespace Karpik.Engine.Core;

public static class EditorSnapshotLimits
{
    public const int MaxEntities = 2_048;
    public const int MaxComponents = 4_096;
    public const int MaxComponentsPerEntity = 64;
    public const int MaxDisplayValueLength = 1_024;
}

public sealed record EditorRuntimeSnapshot
{
    public long CapturedAtUnixMilliseconds { get; init; }
    public int TotalEntityCount { get; init; }
    public bool IsTruncated { get; init; }
    public EditorEntitySnapshot[] Entities { get; init; } = [];

    public byte[] Serialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes(this);
    }

    public static EditorRuntimeSnapshot Deserialize(ReadOnlySpan<byte> payload)
    {
        return JsonSerializer.Deserialize<EditorRuntimeSnapshot>(payload)
               ?? throw new InvalidDataException("Editor snapshot payload is empty.");
    }
}

public sealed record EditorEntitySnapshot
{
    public int EntityId { get; init; }
    public EditorComponentSnapshot[] Components { get; init; } = [];
}

public sealed record EditorComponentSnapshot
{
    public string TypeName { get; init; } = string.Empty;
    public string DisplayValue { get; init; } = string.Empty;
}
