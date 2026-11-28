using System.Collections.Frozen;

namespace Karpik.Engine.Shared.Modding;

public readonly record struct ModDefinition(ModMetaData MetaData, string Directory)
{
    /// <summary>The frozen direct dependency IDs resolved for this side, if available.</summary>
    public FrozenSet<string>? ResolvedDirectDependencies { get; init; }

    /// <summary>The accepted source for this definition's execution side, if already captured.</summary>
    public ModScriptSource? ScriptSource { get; init; }
}