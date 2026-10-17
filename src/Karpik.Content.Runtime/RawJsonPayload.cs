namespace Karpik.Content.Runtime;

/// <summary>
/// Fallback payload for raw-json assets when no [ContentType("raw-json")] is declared.
/// Cooked artifact is canonical JSON bytes; ContentRegistry will materialize this type via JSON deserialize or raw bytes.
/// </summary>
public sealed class RawJsonPayload
{
    public string Json { get; set; } = "{}";
}
