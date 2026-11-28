using System.Collections.Frozen;

namespace Karpik.Engine.Shared.Modding;

/// <summary>Immutable script text and independently comparable content identities for Shared and one execution side.</summary>
public sealed class ModScriptSource(ExecutionSide side, string sharedIdentity, string sideIdentity,
    FrozenDictionary<string, string> files, int byteCount)
{
    public ExecutionSide Side { get; } = side;
    public string SharedIdentity { get; } = sharedIdentity;
    public string SideIdentity { get; } = sideIdentity;
    public int FileCount => files.Count;
    public bool HasSharedFiles => files.Keys.Any(static path => path.StartsWith("Shared/", StringComparison.Ordinal));
    public int ByteCount { get; } = byteCount;

    public bool TryGetFile(string relativePath, out string? text) => files.TryGetValue(relativePath, out text);
}
