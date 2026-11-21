using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

public readonly record struct ModContentOverride(string? TargetModId, AssetId TargetAssetId, AssetId ReplacementAssetId);

internal sealed record ModContentCaptureIdentity(string GenerationId, string ManifestHash, string? DeclarationHash);

/// <summary>Original mod data for cold composition. Does not acquire or release a store claim.</summary>
public sealed class ModContentPackage(string modId, ContentManifest manifest, IContentStore store,
    IEnumerable<ModContentOverride> overrides, string sourcePath)
{
    public string ModId { get; } = modId;
    // Synthetic callers must supply a manifest whose lists they will not mutate.
    public ContentManifest Manifest { get; } = manifest;
    public IContentStore Store { get; } = store is IContentStoreLifetime
        ? store : throw new ArgumentException("A mod content store must support lifetime claims.", nameof(store));
    public IReadOnlyList<ModContentOverride> Overrides { get; } = Array.AsReadOnly(overrides.ToArray());
    public string SourcePath { get; } = sourcePath;
    internal ModContentCaptureIdentity? CaptureIdentity { get; init; }
}
