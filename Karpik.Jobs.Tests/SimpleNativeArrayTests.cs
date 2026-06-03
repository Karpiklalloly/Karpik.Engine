using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class SimpleNativeArrayTests
{
    [Fact]
    public void Indexer_PreservesExistingDenseArrayBehavior()
    {
        using SimpleNativeArray<int> array = new(2);

        array[0] = 10;
        array[1] = 20;
        ref int value = ref array[1];
        value = 30;

        Assert.Equal(10, array[0]);
        Assert.Equal(30, array[1]);
        Assert.Equal(2, array.Length);
    }

    [Fact]
    public void DisposeTwice_Throws()
    {
        SimpleNativeArray<int> array = new(1);
        array.Dispose();

        Exception exception = Assert.Throws<ObjectDisposedException>(() => array.Dispose());
        Assert.IsType<ObjectDisposedException>(exception);
    }
}
