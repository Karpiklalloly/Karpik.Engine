using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding.Bindings;

[Export(typeof(ComponentCallbackScope))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class ComponentCallbackScope : IDisposable
{
    private string? _owner;
    private bool _disposed;

    public void Enter(string owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (_owner is not null) throw new InvalidOperationException("A component callback is already active.");
        _owner = owner;
    }

    public void Exit() => _owner = null;
    internal void Require(string owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_owner != owner) throw new InvalidOperationException("Component access requires its owning system's active callback.");
    }
    public void Dispose() { _owner = null; _disposed = true; }
}
