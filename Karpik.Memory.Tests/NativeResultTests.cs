using Karpik.Memory;
using Xunit;

namespace Karpik.Memory.Tests;

public sealed class NativeResultTests : IDisposable
{
    public void Dispose()
    {
        Assert.Equal(0, NativeMemoryDiagnostics.ActiveAllocationCount);
    }

    [Fact]
    public void Value_ReturnsRefToSingleNativeSlot()
    {
        using NativeResult<int> result = new();

        result.Value = 42;
        ref int value = ref result.Value;
        value = 99;

        Assert.Equal(99, result.Value);
    }

    [Fact]
    public void Handle_ReturnsRefToOwnerStorage()
    {
        using NativeResult<int> result = new();
        NativeResultHandle<int> handle = result.AsHandle();

        handle.Value = 7;
        ref int value = ref handle.Value;
        value = 11;

        Assert.Equal(11, result.Value);
    }

    [Fact]
    public void AccessAfterDispose_Throws()
    {
        NativeResult<int> result = new();
        result.Dispose();

        AssertThrows<ObjectDisposedException>(() => _ = result.Value);
        AssertThrows<ObjectDisposedException>(() => _ = result.AsHandle());
    }

    [Fact]
    public void DisposeTwice_Throws()
    {
        NativeResult<int> result = new();
        result.Dispose();

        AssertThrows<ObjectDisposedException>(() => result.Dispose());
    }

    [Fact]
    public void CopiedHandleAfterOwnerDispose_ThrowsOnAccess()
    {
        NativeResult<int> result = new();
        NativeResultHandle<int> handle = result.AsHandle();
        result.Dispose();

#if DEBUG
        AssertThrows<ObjectDisposedException>(() => _ = handle.Value);
#endif
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        Exception exception = Assert.Throws<TException>(action);
        Assert.IsType<TException>(exception);
    }
}
