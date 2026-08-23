using System.Text.Json;
using System.Text.Json.Serialization;

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
        return JsonSerializer.SerializeToUtf8Bytes(this, typeof(EditorRuntimeSnapshot), EditorSnapshotJsonContext.Default);
    }

    public static EditorRuntimeSnapshot Deserialize(ReadOnlySpan<byte> payload)
    {
        return JsonSerializer.Deserialize(payload, typeof(EditorRuntimeSnapshot), EditorSnapshotJsonContext.Default)
               as EditorRuntimeSnapshot
               ?? throw new InvalidDataException("Editor snapshot payload is empty.");
    }
}

/// <summary>
/// Source-generated JSON contract for the editor IPC snapshot channel.
/// NativeAOT hosts disable reflection-based serialization, so the worker and
/// the editor controller must both resolve types from this context.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(EditorRuntimeSnapshot))]
public sealed partial class EditorSnapshotJsonContext : JsonSerializerContext;

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
