namespace Karpik.Content.Core;

public sealed class ContentProcessorResult
{
    public byte[] CookedBytes { get; }
    public IReadOnlyList<AssetId> Dependencies { get; }
    public IReadOnlyList<ContentDiagnostic> Diagnostics { get; }

    public ContentProcessorResult(byte[] cookedBytes, IReadOnlyList<AssetId> dependencies, IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        CookedBytes = cookedBytes;
        Dependencies = dependencies;
        Diagnostics = diagnostics;
    }
}

public interface IContentProcessor
{
    string DeclaredType { get; }
    string Version { get; }
    ContentProcessorResult Process(ReadOnlySpan<byte> sourceBytes, AssetMeta meta, string relativePath);
}
