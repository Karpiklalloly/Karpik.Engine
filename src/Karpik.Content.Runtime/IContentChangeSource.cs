using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

/// <summary>Changes for the current simulation boundary. Consume on that thread before its next boundary.</summary>
public interface IContentChangeSource
{
    ReadOnlySpan<ContentAssetChange> Changes { get; }
    ulong Sequence { get; }
}

/// <summary>Cold stop request before gameplay destruction; full retirement is awaited after renderer shutdown.</summary>
public interface IContentReloadLifetime
{
    void RequestStop();
}

public enum ContentReloadOutcome { Applied, Superseded, Failed }
public readonly record struct ContentReloadResult(ContentReloadOutcome Outcome, string? Error = null);
