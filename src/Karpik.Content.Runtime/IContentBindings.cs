using System.Text.Json.Serialization.Metadata;
using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

/// <summary>Simulation-owned presentation values. Registration, release, retry and diagnostics are cold APIs.</summary>
public interface IContentBindings
{
    ContentBinding<T> Bind<T>(AssetId id);
    ContentBinding<T> Bind<T>(AssetId id, T fallback);
    ContentBinding<T> BindJson<T>(AssetId id, JsonTypeInfo<T> typeInfo);
    ContentBinding<T> BindJson<T>(AssetId id, JsonTypeInfo<T> typeInfo, T fallback);
    bool TryGet<T>(ContentBinding<T> binding, out T value);
    bool TryGetStatus<T>(ContentBinding<T> binding, out ContentBindingStatus status);
    bool Release<T>(ContentBinding<T> binding);
    bool Retry<T>(ContentBinding<T> binding);
    Exception? GetError<T>(ContentBinding<T> binding);
}

/// <summary>Host-only boundary and cold shutdown integration for the same scoped binding owner.</summary>
public interface IContentBindingRuntime : IAsyncDisposable
{
    void AdvanceBoundary();
    void RequestStop();
}
