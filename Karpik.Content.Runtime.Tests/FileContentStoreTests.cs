using System.IO;
using Karpik.Content.Runtime;
using Xunit;

public sealed class FileContentStoreTests
{
    [Fact]
    public void Get_Missing_Throws()
    {
        var store = new FileContentStore(Path.GetTempPath());
        Assert.Throws<InvalidDataException>(() => { store.Get("artifacts/ab/cd/hash.cooked"); });
    }
}
