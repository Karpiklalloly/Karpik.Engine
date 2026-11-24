using System.Collections.Frozen;

namespace Karpik.Engine.Shared.Modding;

public readonly record struct ModDefinition(ModMetaData MetaData, string Directory)
{
    /// <summary>The frozen direct dependency IDs resolved at startup for this side, if available.</summary>
    public FrozenSet<string>? ResolvedDirectDependencies { get; init; }
}