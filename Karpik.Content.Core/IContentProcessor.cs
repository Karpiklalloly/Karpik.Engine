namespace Karpik.Content.Core;

public sealed class ContentProcessorResult(
    byte[] cookedBytes,
    IReadOnlyList<AssetId> dependencies,
    IReadOnlyList<ContentDiagnostic> diagnostics)
{
    public byte[] CookedBytes { get; } = cookedBytes;
    public IReadOnlyList<AssetId> Dependencies { get; } = dependencies;
    public IReadOnlyList<ContentDiagnostic> Diagnostics { get; } = diagnostics;
}

public interface IContentProcessor
{
    string DeclaredType { get; }
    string Version { get; }
    ContentProcessorResult Process(ReadOnlySpan<byte> sourceBytes, AssetMeta meta, string relativePath);
}
