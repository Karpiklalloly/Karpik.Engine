using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding.Bindings;

/// <summary>An opaque pointer value. This value neither owns nor extends the lifetime of its pointee.</summary>
public readonly struct ComponentPointerValue
{
    internal readonly nuint Address;
    internal readonly Guid RegistryId;
    internal readonly int TypeSlot;
    internal readonly int DomainSlot;
    internal readonly uint Generation;

    internal ComponentPointerValue(nuint address, Guid registryId, int typeSlot, int domainSlot, uint generation)
    {
        Address = address;
        RegistryId = registryId;
        TypeSlot = typeSlot;
        DomainSlot = domainSlot;
        Generation = generation;
    }

    public bool IsNull => Address == 0;
    public override string ToString() => IsNull ? "null pointer" : "opaque pointer";
}

/// <summary>A host-created native lifetime domain, belonging to exactly one Simulation registry.</summary>
public readonly struct ComponentPointerDomain
{
    internal readonly Guid RegistryId;
    internal readonly int Slot;
    internal ComponentPointerDomain(Guid registryId, int slot) { RegistryId = registryId; Slot = slot; }
}

/// <summary>A prepared host pointer type. Resolve this handle once before entering a hot path.</summary>
public readonly struct ComponentPointerType
{
    internal readonly Guid RegistryId;
    internal readonly int Slot;
    internal ComponentPointerType(Guid registryId, int slot) { RegistryId = registryId; Slot = slot; }
}

/// <summary>
/// Explicit host permission and lifetime checks for opaque native pointers. Registration is preparation work;
/// prepared-handle capture, validation and address access allocate no managed memory. The host must revoke a
/// domain before freeing its native memory. A successful check cannot prove that arbitrary native memory lives.
/// This service is used from the sequential Simulation owner; it does not synchronize concurrent host mutations.
/// </summary>
[Export(typeof(ComponentPointerRegistry))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class ComponentPointerRegistry : IDisposable
{
    private readonly Guid _identity = Guid.NewGuid();
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, int> _byNativeType = new();
    private DomainState[] _domains = new DomainState[4];
    private TypeState[] _types = new TypeState[4];
    private int _domainCount;
    private int _typeCount;
    private bool _frozen;
    private bool _disposed;

    public bool IsFrozen => _frozen;

    public ComponentPointerDomain CreateDomain()
    {
        RequireRegistration();
        if (_domainCount == _domains.Length) Array.Resize(ref _domains, checked(_domains.Length * 2));
        _domains[_domainCount] = new DomainState { Generation = 1, Active = true };
        return new ComponentPointerDomain(_identity, ++_domainCount);
    }

    public ComponentPointerType RegisterType(string id, Type nativePointerType, ComponentPointerDomain domain)
    {
        RequireRegistration();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(nativePointerType);
        for (int i = 0; i < id.Length; i++)
            if (char.IsWhiteSpace(id[i])) throw new ArgumentException("A pointer type ID cannot contain whitespace.", nameof(id));
        if (!nativePointerType.IsPointer)
            throw new ArgumentException("A native pointer binding requires a CLR pointer type.", nameof(nativePointerType));
        ref readonly DomainState state = ref RequireDomain(domain);
        if (!state.Active) throw new InvalidOperationException("Pointer lifetime domain has been revoked.");
        if (_byName.ContainsKey(id)) throw new InvalidOperationException($"Pointer type '{id}' is already registered.");
        if (_byNativeType.ContainsKey(nativePointerType))
            throw new InvalidOperationException("A CLR pointer type already has a registered host binding.");
        if (_typeCount == _types.Length) Array.Resize(ref _types, checked(_types.Length * 2));
        int slot = _typeCount + 1;
        _types[_typeCount++] = new TypeState(domain.Slot, state.Generation);
        _byName.Add(id, slot);
        _byNativeType.Add(nativePointerType, slot);
        return new ComponentPointerType(_identity, slot);
    }

    public ComponentPointerType ResolveType(string id)
    {
        RequireAlive();
        ArgumentNullException.ThrowIfNull(id);
        return _byName.TryGetValue(id, out int slot)
            ? new ComponentPointerType(_identity, slot)
            : throw new InvalidDataException($"Pointer type '{id}' has not been allowed by the host.");
    }

    public ComponentPointerType ResolveNativeType(Type nativePointerType)
    {
        RequireAlive();
        ArgumentNullException.ThrowIfNull(nativePointerType);
        return _byNativeType.TryGetValue(nativePointerType, out int slot)
            ? new ComponentPointerType(_identity, slot)
            : throw new InvalidDataException("The native pointer type has not been allowed by the host.");
    }

    public void Freeze()
    {
        RequireAlive();
        _frozen = true;
    }

    public ComponentPointerValue Capture(ComponentPointerType type, nuint rawAddress)
    {
        ref readonly TypeState binding = ref RequireType(type);
        // Clearing an opted-in pointer is safe even after the host revoked its pointee domain.
        if (rawAddress == 0) return default;
        RequireActive(binding.DomainSlot, binding.Generation);
        return new ComponentPointerValue(rawAddress, _identity, type.Slot, binding.DomainSlot, binding.Generation);
    }

    public ComponentPointerValue Capture(string typeId, nuint rawAddress) => Capture(ResolveType(typeId), rawAddress);

    public void Validate(ComponentPointerValue value)
    {
        RequireAlive();
        if (value.IsNull) return;
        ValidateNonNull(value);
    }

    public void ValidateAssignment(ComponentPointerValue value, ComponentPointerType targetType)
    {
        _ = RequireType(targetType);
        if (value.IsNull) return;
        if (value.RegistryId != _identity || value.TypeSlot != targetType.Slot)
            throw new InvalidOperationException("Pointer token belongs to a different Simulation or pointer type.");
        ValidateNonNull(value);
    }

    public void ValidateAssignment(ComponentPointerValue value, string targetTypeId) =>
        ValidateAssignment(value, ResolveType(targetTypeId));

    /// <summary>Returns an address only to trusted CLR callers after permission and lifetime checks.</summary>
    public nuint ReadAddress(ComponentPointerValue value, ComponentPointerType targetType)
    {
        ValidateAssignment(value, targetType);
        return value.Address;
    }

    public nuint ReadAddress(ComponentPointerValue value, string targetTypeId) => ReadAddress(value, ResolveType(targetTypeId));

    public void Revoke(ComponentPointerDomain domain)
    {
        ref DomainState state = ref RequireDomain(domain);
        if (!state.Active) return;
        state.Active = false;
        state.Generation++;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _byName.Clear();
        _byNativeType.Clear();
        _domains = [];
        _types = [];
        _domainCount = 0;
        _typeCount = 0;
    }

    private void ValidateNonNull(ComponentPointerValue value)
    {
        if (value.RegistryId != _identity || (uint)(value.TypeSlot - 1) >= (uint)_typeCount)
            throw new InvalidOperationException("Pointer token belongs to a different Simulation or pointer type.");
        ref readonly TypeState binding = ref _types[value.TypeSlot - 1];
        if (binding.DomainSlot != value.DomainSlot || binding.Generation != value.Generation)
            throw new InvalidOperationException("Pointer token has an incompatible lifetime domain.");
        RequireActive(value.DomainSlot, value.Generation);
    }

    private ref readonly TypeState RequireType(ComponentPointerType type)
    {
        RequireAlive();
        if (type.RegistryId != _identity || (uint)(type.Slot - 1) >= (uint)_typeCount)
            throw new InvalidOperationException("Pointer type has not been registered in this Simulation.");
        return ref _types[type.Slot - 1];
    }

    private ref DomainState RequireDomain(ComponentPointerDomain domain)
    {
        RequireAlive();
        if (domain.RegistryId != _identity || (uint)(domain.Slot - 1) >= (uint)_domainCount)
            throw new InvalidOperationException("Pointer lifetime domain belongs to another Simulation or is invalid.");
        return ref _domains[domain.Slot - 1];
    }

    private void RequireActive(int slot, uint generation)
    {
        if ((uint)(slot - 1) >= (uint)_domainCount || !_domains[slot - 1].Active || _domains[slot - 1].Generation != generation)
            throw new InvalidOperationException("Pointer lifetime domain has been revoked.");
    }

    private void RequireAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void RequireRegistration()
    {
        RequireAlive();
        if (_frozen) throw new InvalidOperationException("Pointer type and domain registrations are frozen.");
    }

    private struct DomainState
    {
        internal uint Generation;
        internal bool Active;
    }

    private readonly record struct TypeState(int DomainSlot, uint Generation);
}
