using System.Runtime.CompilerServices;
using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;

public sealed class PointerPolicyTests
{
    private static Type IntPointer => typeof(int).MakePointerType();
    private static Type VoidPointer => typeof(void).MakePointerType();

    [Fact]
    public void PointerPolicy_RequiresExplicitHostRegistration()
    {
        using var registry = new ComponentPointerRegistry();
        Assert.Throws<InvalidDataException>(() => registry.ResolveType("host:Buffer"));
        Assert.Throws<InvalidDataException>(() => registry.ResolveNativeType(IntPointer));
        Assert.Throws<InvalidOperationException>(() => registry.Capture(default(ComponentPointerType), 16));
        Assert.Throws<ArgumentException>(() => registry.RegisterType("host:Bad", typeof(int), registry.CreateDomain()));
        Assert.Throws<ArgumentException>(() => registry.RegisterType(" ", IntPointer, registry.CreateDomain()));
    }

    [Fact]
    public void PointerPolicy_TokensAndHandlesAreUnmanagedAndNullIsDefault()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ComponentPointerValue>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ComponentPointerDomain>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ComponentPointerType>());
        Assert.True(default(ComponentPointerValue).IsNull);
    }

    [Fact]
    public void PointerPolicy_RoundTripChecksRegisteredTypeAndNeverDereferences()
    {
        using var registry = new ComponentPointerRegistry();
        var domain = registry.CreateDomain();
        var type = registry.RegisterType("host:Buffer", IntPointer, domain);
        var other = registry.RegisterType("host:Untyped", VoidPointer, domain);
        Assert.Equal(type, registry.ResolveType("host:Buffer"));
        Assert.Equal(type, registry.ResolveNativeType(IntPointer));
        // Synthetic address is deliberately not dereferenceable; the policy only transports it.
        var value = registry.Capture(type, 16);
        Assert.False(value.IsNull);
        registry.Validate(value);
        registry.ValidateAssignment(value, type);
        Assert.Equal((nuint)16, registry.ReadAddress(value, type));
        Assert.Throws<InvalidOperationException>(() => registry.ValidateAssignment(value, other));
        Assert.Throws<InvalidOperationException>(() => registry.ReadAddress(value, other));
        Assert.DoesNotContain("16", value.ToString());
    }

    [Fact]
    public void PointerPolicy_RevocationInvalidatesRetainedValuesButAllowsClearing()
    {
        using var registry = new ComponentPointerRegistry();
        var domain = registry.CreateDomain();
        var type = registry.RegisterType("host:Buffer", IntPointer, domain);
        var otherDomain = registry.CreateDomain();
        var otherType = registry.RegisterType("host:Other", VoidPointer, otherDomain);
        var retained = registry.Capture(type, 16);
        var stillAlive = registry.Capture(otherType, 32);
        registry.Revoke(domain); registry.Revoke(domain);
        Assert.Throws<InvalidOperationException>(() => registry.Validate(retained));
        Assert.Throws<InvalidOperationException>(() => registry.ValidateAssignment(retained, type));
        Assert.Throws<InvalidOperationException>(() => registry.Capture(type, 16));
        registry.Validate(stillAlive);
        Assert.Equal((nuint)32, registry.ReadAddress(stillAlive, otherType));
        registry.ValidateAssignment(default, type);
        Assert.True(registry.Capture(type, 0).IsNull);
        Assert.Equal((nuint)0, registry.ReadAddress(default, type));
    }

    [Fact]
    public void PointerPolicy_RejectsForeignSimulationTokensAndDomainHandles()
    {
        using var first = new ComponentPointerRegistry(); using var second = new ComponentPointerRegistry();
        var firstDomain = first.CreateDomain(); var secondDomain = second.CreateDomain();
        var firstType = first.RegisterType("host:Buffer", IntPointer, firstDomain);
        var secondType = second.RegisterType("host:Buffer", IntPointer, secondDomain);
        var value = first.Capture(firstType, 16);
        Assert.Throws<InvalidOperationException>(() => second.Validate(value));
        Assert.Throws<InvalidOperationException>(() => second.ValidateAssignment(value, secondType));
        Assert.Throws<InvalidOperationException>(() => second.Capture(firstType, 16));
        Assert.Throws<InvalidOperationException>(() => second.RegisterType("host:Foreign", VoidPointer, firstDomain));
        Assert.Throws<InvalidOperationException>(() => second.Revoke(firstDomain));
    }

    [Fact]
    public void PointerPolicy_DuplicateOrFrozenRegistrationsCannotChangeMeaning()
    {
        using var registry = new ComponentPointerRegistry();
        var domain = registry.CreateDomain();
        var type = registry.RegisterType("host:Buffer", IntPointer, domain);
        Assert.Throws<InvalidOperationException>(() => registry.RegisterType("host:Buffer", VoidPointer, domain));
        Assert.Throws<InvalidOperationException>(() => registry.RegisterType("host:Alias", IntPointer, domain));
        registry.Freeze(); registry.Freeze();
        Assert.Throws<InvalidOperationException>(() => registry.RegisterType("host:Other", VoidPointer, domain));
        Assert.Throws<InvalidOperationException>(() => registry.CreateDomain());
        Assert.Equal(type, registry.ResolveNativeType(IntPointer));
        registry.Revoke(domain);
    }

    [Fact]
    public void PointerPolicy_DisposeRejectsAllRegistryOperations()
    {
        var registry = new ComponentPointerRegistry(); var domain = registry.CreateDomain();
        var type = registry.RegisterType("host:Buffer", IntPointer, domain); var value = registry.Capture(type, 16);
        registry.Dispose(); registry.Dispose();
        Assert.Throws<ObjectDisposedException>(() => registry.Validate(value));
        Assert.Throws<ObjectDisposedException>(() => registry.Validate(default));
        Assert.Throws<ObjectDisposedException>(() => registry.ReadAddress(value, type));
        Assert.Throws<ObjectDisposedException>(() => registry.Capture(type, 16));
        Assert.Throws<ObjectDisposedException>(() => registry.ResolveType("host:Buffer"));
        Assert.Throws<ObjectDisposedException>(() => registry.CreateDomain());
        Assert.Throws<ObjectDisposedException>(() => registry.Revoke(domain));
        Assert.Throws<ObjectDisposedException>(() => registry.Freeze());
    }

    [Fact]
    public void PointerPolicy_ReservedCaptureValidationAndReadAllocateZero()
    {
        using var registry = new ComponentPointerRegistry();
        var type = registry.RegisterType("host:Buffer", IntPointer, registry.CreateDomain()); registry.Freeze();
        for (int i = 0; i < 2000; i++) Run();
        long before = GC.GetAllocatedBytesForCurrentThread();
        nuint result = 0;
        for (int i = 0; i < 1000; i++) result = Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal((nuint)16, result);
        nuint Run()
        {
            var value = registry.Capture(type, 16);
            registry.Validate(value); registry.ValidateAssignment(value, type);
            return registry.ReadAddress(value, type);
        }
    }
}
