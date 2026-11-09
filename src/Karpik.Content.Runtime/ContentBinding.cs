using DCFApixels.DragonECS;
using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Content.Runtime;

/// <summary>An owner-local, generation-checked presentation binding. Copies do not acquire ownership.</summary>
public readonly struct ContentBinding<T> : IEcsComponent, IEquatable<ContentBinding<T>>
{
    internal readonly Guid Owner;
    internal readonly int Slot;
    internal readonly uint Generation;

    internal ContentBinding(Guid owner, int slot, uint generation)
    {
        Owner = owner;
        Slot = slot;
        Generation = generation;
    }

    public bool Equals(ContentBinding<T> other) => Owner == other.Owner && Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is ContentBinding<T> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Owner, Slot, Generation);
    public static bool operator ==(ContentBinding<T> left, ContentBinding<T> right) => left.Equals(right);
    public static bool operator !=(ContentBinding<T> left, ContentBinding<T> right) => !left.Equals(right);
}

public enum ContentBindingState { Pending, Ready, Missing, Failed }

/// <summary>A value-only observation; availability is independent of the loading state.</summary>
public readonly record struct ContentBindingStatus(
    ContentBindingState State, bool HasValue, bool IsFallback, uint RequestedVersion, uint PresentedVersion);

/// <summary>Capacity chosen before presentation starts; buffers never grow in a frame.</summary>
[Export(typeof(ContentBindingOptions))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed record ContentBindingOptions
{
    public const int DefaultCapacity = 256;
    public const int MaximumCapacity = 4096;
    public int Capacity { get; init; } = DefaultCapacity;
}
